using System.Net;
using System.Net.Http.Json;
using CraftQuotes.Api.Domain;
using Shouldly;

namespace CraftQuotes.IntegrationTests;

public sealed class QuoteLifecycleTests(ApiFactory api) : ApiTest(api)
{
    [Fact]
    public async Task Full_lifecycle_create_get_update_send_accept_with_If_Match_throughout()
    {
        var created = await CreateAsync(Content(lines: KariLines[..4]));
        created.Status.ShouldBe(QuoteStatus.Draft);
        created.Version.ShouldBe(1);
        created.Number.ShouldMatch(@"^Q-\d{6}$");

        var get = await Client.GetAsync($"/api/quotes/{created.Id}", Ct);
        get.StatusCode.ShouldBe(HttpStatusCode.OK);
        get.Headers.ETag!.ToString().ShouldBe("\"1\"");

        var updated = await SendAsync(HttpMethod.Put, $"/api/quotes/{created.Id}", "\"1\"", Content());
        updated.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ReadQuoteAsync(updated)).Lines.Count.ShouldBe(5);

        var sent = await SendAsync(HttpMethod.Post, $"/api/quotes/{created.Id}/send", "\"2\"");
        sent.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ReadQuoteAsync(sent)).Status.ShouldBe(QuoteStatus.Sent);

        var accepted = await SendAsync(HttpMethod.Post, $"/api/quotes/{created.Id}/accept", "\"3\"");
        accepted.StatusCode.ShouldBe(HttpStatusCode.OK);
        var final = await ReadQuoteAsync(accepted);
        final.Status.ShouldBe(QuoteStatus.Accepted);
        final.Version.ShouldBe(4);
        final.SentAt.ShouldBe(ApiFactory.StartTime);
        final.DecidedAt.ShouldBe(ApiFactory.StartTime);
    }

    [Fact]
    public async Task Create_computes_Kari_totals_and_applies_POST_defaults()
    {
        var body = new
        {
            title = "EV charger installation",
            customer = new { name = "Kari Nordmann", email = "kari@example.com" },
            lines = KariLines,
        };

        var quote = await CreateAsync(body);

        (quote.Subtotal, quote.VatTotal, quote.Total).ShouldBe((18_215.00m, 4_553.75m, 22_768.75m));
        quote.Currency.ShouldBe("NOK");
        quote.ValidUntil.ShouldBe(Today.AddDays(30));
        quote.Lines.Select(l => l.Position).ShouldBe([1, 2, 3, 4, 5]);
    }

    [Fact]
    public async Task ETag_is_only_on_GET_mutation_responses_carry_version_in_body()
    {
        var response = await Client.PostAsJsonAsync("/api/quotes", Content(), Ct);
        response.Headers.ETag.ShouldBeNull();
        response.Headers.Location!.ToString().ShouldStartWith("/api/quotes/");
        var created = await ReadQuoteAsync(response);

        var updated = await SendAsync(HttpMethod.Put, $"/api/quotes/{created.Id}", "\"1\"", Content());

        updated.Headers.ETag.ShouldBeNull();
        (await ReadQuoteAsync(updated)).Version.ShouldBe(2);
    }

    [Fact]
    public async Task PUT_is_a_full_replace_omitted_optional_fields_are_cleared()
    {
        var created = await CreateAsync(new
        {
            title = "With description",
            description = "Garage wall mount",
            customer = new { name = "Kari Nordmann", email = "kari@example.com", phone = "+47 900 00 000" },
            lines = KariLines,
        });

        var response = await SendAsync(HttpMethod.Put, $"/api/quotes/{created.Id}", "\"1\"", new
        {
            title = "Without description",
            customer = new { name = "Kari Nordmann", email = "kari@example.com" },
            validUntil = Today.AddDays(5),
            lines = Array.Empty<object>(),
        });

        var replaced = await ReadQuoteAsync(response);
        replaced.Description.ShouldBeNull();
        replaced.Customer.Phone.ShouldBeNull();
        replaced.Lines.ShouldBeEmpty();
        replaced.Total.ShouldBe(0m);
    }

    [Fact]
    public async Task Unknown_id_is_404_for_get_and_mutations()
    {
        var unknown = $"/api/quotes/{Guid.CreateVersion7()}";

        await ShouldBeProblemAsync(await Client.GetAsync(unknown, Ct), HttpStatusCode.NotFound);
        await ShouldBeProblemAsync(await SendAsync(HttpMethod.Put, unknown, "\"1\"", Content()), HttpStatusCode.NotFound);
        await ShouldBeProblemAsync(await SendAsync(HttpMethod.Post, $"{unknown}/send", "\"1\""), HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task R2_Edit_after_send_is_409_not_editable()
    {
        var sent = await CreateSentAsync();

        var response = await SendAsync(HttpMethod.Put, $"/api/quotes/{sent.Id}", "\"2\"", Content());

        var problem = await ShouldBeProblemAsync(response, HttpStatusCode.Conflict);
        problem.GetProperty("errorCode").GetString().ShouldBe(QuoteErrors.NotEditable);
    }

    [Fact]
    public async Task D19_PUT_on_sent_quote_with_stale_tag_is_409_not_412()
    {
        var sent = await CreateSentAsync(); // now version 2

        var response = await SendAsync(HttpMethod.Put, $"/api/quotes/{sent.Id}", "\"1\"", Content());

        var problem = await ShouldBeProblemAsync(response, HttpStatusCode.Conflict);
        problem.GetProperty("errorCode").GetString().ShouldBe(QuoteErrors.NotEditable);
    }

    [Fact]
    public async Task Stale_send_is_412_and_the_quote_stays_Draft()
    {
        // Ola reviews version 1; the office manager saves a new price (version 2); Ola presses Send.
        var reviewed = await CreateAsync();
        var managerEdit = await SendAsync(
            HttpMethod.Put, $"/api/quotes/{reviewed.Id}", "\"1\"", Content(lines: [Line(unitPrice: 990)]));
        managerEdit.StatusCode.ShouldBe(HttpStatusCode.OK);

        var send = await SendAsync(HttpMethod.Post, $"/api/quotes/{reviewed.Id}/send", "\"1\"");

        await ShouldBeProblemAsync(send, HttpStatusCode.PreconditionFailed);
        var current = await ReadQuoteAsync(await Client.GetAsync($"/api/quotes/{reviewed.Id}", Ct));
        current.Status.ShouldBe(QuoteStatus.Draft);
        current.Version.ShouldBe(2);
        current.SentAt.ShouldBeNull();
    }

    [Fact]
    public async Task R3_Send_without_lines_is_409_no_lines()
    {
        var empty = await CreateAsync(Content(lines: []));

        var response = await SendAsync(HttpMethod.Post, $"/api/quotes/{empty.Id}/send", "\"1\"");

        (await ShouldBeProblemAsync(response, HttpStatusCode.Conflict))
            .GetProperty("errorCode").GetString().ShouldBe(QuoteErrors.NoLines);
    }

    [Fact]
    public async Task R4_Send_with_past_validity_is_409_valid_until_in_past()
    {
        var stale = await CreateAsync(Content(validUntil: Today.AddDays(-1)));

        var response = await SendAsync(HttpMethod.Post, $"/api/quotes/{stale.Id}/send", "\"1\"");

        (await ShouldBeProblemAsync(response, HttpStatusCode.Conflict))
            .GetProperty("errorCode").GetString().ShouldBe(QuoteErrors.ValidUntilInPast);
    }

    [Fact]
    public async Task R6_Accept_after_expiry_is_409_expired_but_reject_still_works()
    {
        var sent = await CreateSentAsync(validUntil: Today);
        Api.Time.Advance(TimeSpan.FromHours(12)); // 22:00 UTC = midnight in Oslo: the next business date

        var accept = await SendAsync(HttpMethod.Post, $"/api/quotes/{sent.Id}/accept", "\"2\"");
        (await ShouldBeProblemAsync(accept, HttpStatusCode.Conflict))
            .GetProperty("errorCode").GetString().ShouldBe(QuoteErrors.Expired);

        var reject = await SendAsync(
            HttpMethod.Post, $"/api/quotes/{sent.Id}/reject", "\"2\"", new { reason = "Too late" });
        reject.StatusCode.ShouldBe(HttpStatusCode.OK);
        var rejected = await ReadQuoteAsync(reject);
        rejected.Status.ShouldBe(QuoteStatus.Rejected);
        rejected.RejectionReason.ShouldBe("Too late");
    }

    [Fact]
    public async Task R6_Accept_on_the_last_valid_business_day_succeeds()
    {
        var sent = await CreateSentAsync(validUntil: Today);
        Api.Time.Advance(TimeSpan.FromHours(12) - TimeSpan.FromSeconds(1)); // 23:59:59 in Oslo

        var accept = await SendAsync(HttpMethod.Post, $"/api/quotes/{sent.Id}/accept", "\"2\"");

        accept.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task R5_Reject_accepts_an_empty_body()
    {
        var sent = await CreateSentAsync();

        var response = await SendAsync(HttpMethod.Post, $"/api/quotes/{sent.Id}/reject", "\"2\"");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ReadQuoteAsync(response)).RejectionReason.ShouldBeNull();
    }

    [Fact]
    public async Task R7_Accepted_quote_cannot_be_rejected()
    {
        var sent = await CreateSentAsync();
        (await SendAsync(HttpMethod.Post, $"/api/quotes/{sent.Id}/accept", "\"2\"")).StatusCode.ShouldBe(HttpStatusCode.OK);

        var reject = await SendAsync(HttpMethod.Post, $"/api/quotes/{sent.Id}/reject", "\"3\"");

        (await ShouldBeProblemAsync(reject, HttpStatusCode.Conflict))
            .GetProperty("errorCode").GetString().ShouldBe(QuoteErrors.InvalidTransition);
    }
}
