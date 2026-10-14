using System.Net;
using System.Net.Http.Json;
using CraftQuotes.Api.Domain;
using CraftQuotes.Api.Features.Quotes;
using Shouldly;

namespace CraftQuotes.IntegrationTests;

public sealed class ListQuotesTests(ApiFactory api) : ApiTest(api)
{
    private async Task<PagedResponse<QuoteSummaryResponse>> ListAsync(string query) =>
        (await Client.GetFromJsonAsync<PagedResponse<QuoteSummaryResponse>>($"/api/quotes{query}", Json, Ct))!;

    [Fact]
    public async Task Lists_newest_first_with_paging_metadata()
    {
        for (var i = 1; i <= 3; i++)
        {
            await CreateAsync(Content(title: $"Quote {i}"));
            Api.Time.Advance(TimeSpan.FromMinutes(1));
        }

        var page1 = await ListAsync("?page=1&pageSize=2");
        var page2 = await ListAsync("?page=2&pageSize=2");

        page1.Items.Select(q => q.Title).ShouldBe(["Quote 3", "Quote 2"]);
        page2.Items.Select(q => q.Title).ShouldBe(["Quote 1"]);
        (page1.Page, page1.PageSize, page1.TotalCount).ShouldBe((1, 2, 3));
        page1.Items[0].Total.ShouldBe(22_768.75m);
        page1.Items[0].Currency.ShouldBe("NOK");
    }

    [Fact]
    public async Task Filters_by_status()
    {
        await CreateAsync();
        var sent = await CreateSentAsync();

        var result = await ListAsync("?status=Sent");

        result.Items.Select(q => q.Id).ShouldBe([sent.Id]);
        result.TotalCount.ShouldBe(1);
    }

    [Fact]
    public async Task Search_matches_title_or_customer_name_case_insensitively_and_literally()
    {
        var byTitle = await CreateAsync(Content(title: "Bathroom 100% refit", customerName: "Ola Hansen"));
        var byCustomer = await CreateAsync(Content(title: "EV charger", customerName: "Kari Nordmann"));
        await CreateAsync(Content(title: "Cable 1000 m", customerName: "Per Olsen"));

        (await ListAsync("?search=KARI")).Items.Select(q => q.Id).ShouldBe([byCustomer.Id]);
        (await ListAsync("?search=bathroom")).Items.Select(q => q.Id).ShouldBe([byTitle.Id]);
        // "%" and "_" are matched literally; as wildcards, "100%" would also match "Cable 1000 m" and "_" every row.
        (await ListAsync("?search=100%25")).Items.Select(q => q.Id).ShouldBe([byTitle.Id]);
        (await ListAsync("?search=_")).Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task Summary_isExpired_follows_the_business_date()
    {
        await CreateSentAsync(validUntil: Today);
        (await ListAsync("")).Items.Single().IsExpired.ShouldBeFalse();

        Api.Time.Advance(TimeSpan.FromHours(12)); // midnight in Oslo

        var item = (await ListAsync("")).Items.Single();
        item.IsExpired.ShouldBeTrue();
        item.Status.ShouldBe(QuoteStatus.Sent);
    }

    [Theory]
    [InlineData("?status=1")]
    [InlineData("?status=Unknown")]
    [InlineData("?page=0")]
    [InlineData("?pageSize=101")]
    [InlineData("?page=abc")]
    [InlineData("?page=21474837")]      // (page - 1) * pageSize would overflow int
    [InlineData("?search=a%00b")]       // NUL cannot be stored or matched by Postgres text
    [InlineData("?status=Draft,Sent")]  // flag-style combinations are not a status
    public async Task Invalid_query_is_400(string query)
    {
        var response = await Client.GetAsync($"/api/quotes{query}", Ct);

        await ShouldBeProblemAsync(response, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Largest_allowed_page_is_answered_without_overflow()
    {
        var result = await ListAsync($"?page={int.MaxValue / 100}&pageSize=100");

        result.Items.ShouldBeEmpty();
    }
}
