using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Shouldly;

namespace CraftQuotes.IntegrationTests;

public sealed class ValidationTests(ApiFactory api) : ApiTest(api)
{
    private const string ValidCustomer = """{ "name": "Kari Nordmann", "email": "kari@example.com" }""";

    [Fact]
    public async Task Missing_customer_is_400()
    {
        var response = await Client.PostAsJsonAsync("/api/quotes", new { title = "No customer", lines = KariLines }, Ct);

        var problem = await ShouldBeProblemAsync(response, HttpStatusCode.BadRequest);
        problem.GetProperty("errors").TryGetProperty("customer", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task Line_without_vatRate_is_400_never_defaulted()
    {
        var response = await PostRawJsonAsync("/api/quotes", $$"""
            { "title": "x", "customer": {{ValidCustomer}},
              "lines": [ { "type": "Labour", "description": "Work", "quantity": 1, "unit": "h", "unitPrice": 950 } ] }
            """);

        var problem = await ShouldBeProblemAsync(response, HttpStatusCode.BadRequest);
        problem.GetProperty("errors").TryGetProperty("lines[0].vatRate", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task Null_line_is_400()
    {
        var response = await PostRawJsonAsync("/api/quotes", $$"""{ "title": "x", "customer": {{ValidCustomer}}, "lines": [ null ] }""");

        var problem = await ShouldBeProblemAsync(response, HttpStatusCode.BadRequest);
        problem.GetProperty("errors").TryGetProperty("lines[0]", out _).ShouldBeTrue();
    }

    [Theory]
    [InlineData("quantity", "1.0001")]
    [InlineData("unitPrice", "949.999")]
    [InlineData("vatRate", "0.12345")]
    public async Task R9_Too_many_decimals_is_400_not_rounded(string field, string value)
    {
        var line = $$"""{ "type": "Labour", "description": "Work", "quantity": 1, "unit": "h", "unitPrice": 950, "vatRate": 0.25, "{{field}}": {{value}} }""";

        var response = await PostRawJsonAsync("/api/quotes", $$"""{ "title": "x", "customer": {{ValidCustomer}}, "lines": [ {{line}} ] }""");

        var problem = await ShouldBeProblemAsync(response, HttpStatusCode.BadRequest);
        problem.GetProperty("errors").TryGetProperty($"lines[0].{field}", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task R9_Trailing_zeros_are_valid()
    {
        var response = await PostRawJsonAsync("/api/quotes", $$"""
            { "title": "x", "customer": {{ValidCustomer}},
              "lines": [ { "type": "Labour", "description": "Work", "quantity": 5.000, "unit": "h", "unitPrice": 950.000, "vatRate": 0.2500 } ] }
            """);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await ReadQuoteAsync(response)).Subtotal.ShouldBe(4_750.00m);
    }

    [Fact]
    public async Task PUT_without_lines_is_400_because_replace_has_no_defaults()
    {
        var quote = await CreateAsync();

        var response = await SendAsync(HttpMethod.Put, $"/api/quotes/{quote.Id}", "\"1\"", new
        {
            title = "x",
            customer = new { name = "Kari Nordmann", email = "kari@example.com" },
            validUntil = Today,
        });

        var problem = await ShouldBeProblemAsync(response, HttpStatusCode.BadRequest);
        problem.GetProperty("errors").TryGetProperty("lines", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task Rejection_reason_too_long_is_400()
    {
        var sent = await CreateSentAsync();

        var response = await SendAsync(
            HttpMethod.Post, $"/api/quotes/{sent.Id}/reject", "\"2\"", new { reason = new string('x', 501) });

        var problem = await ShouldBeProblemAsync(response, HttpStatusCode.BadRequest);
        problem.GetProperty("errors").TryGetProperty("reason", out _).ShouldBeTrue();
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")] // no ThrowOnBadRequest: an empty 400 that UseStatusCodePages fills
    public async Task Binding_failures_are_400_ProblemDetails_in_every_environment(string environment)
    {
        using var host = Api.WithWebHostBuilder(builder => builder.UseEnvironment(environment));
        using var client = host.CreateClient();

        var malformed = await client.PostAsync(
            "/api/quotes", new StringContent("""{ "title": "x", """, System.Text.Encoding.UTF8, "application/json"), Ct);
        var integerEnum = await client.PostAsync(
            "/api/quotes",
            new StringContent(
                $$"""{ "title": "x", "customer": {{ValidCustomer}}, "lines": [ { "type": 0, "description": "Work", "quantity": 1, "unit": "h", "unitPrice": 1, "vatRate": 0.25 } ] }""",
                System.Text.Encoding.UTF8,
                "application/json"),
            Ct);

        await ShouldBeProblemAsync(malformed, HttpStatusCode.BadRequest);
        await ShouldBeProblemAsync(integerEnum, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Negative_zero_price_and_vat_rate_are_accepted_as_zero()
    {
        var response = await PostRawJsonAsync("/api/quotes", $$"""
            { "title": "x", "customer": {{ValidCustomer}},
              "lines": [ { "type": "Labour", "description": "Free check", "quantity": 1, "unit": "h", "unitPrice": -0.00, "vatRate": -0 } ] }
            """);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var line = (await ReadQuoteAsync(response)).Lines.Single();
        (line.UnitPrice, line.VatRate, line.TotalAmount).ShouldBe((0m, 0m, 0m));
    }

    [Theory]
    [InlineData("title")]
    [InlineData("customer.name")]
    [InlineData("lines[0].description")]
    public async Task NUL_character_in_text_is_400_not_a_database_error(string field)
    {
        // The JSON escape \u0000 (valid JSON); a raw NUL byte would already fail JSON parsing.
        string Text(string name) => name == field ? @"a\u0000b" : "x";
        var response = await PostRawJsonAsync("/api/quotes", $$"""
            { "title": "{{Text("title")}}", "customer": { "name": "{{Text("customer.name")}}", "email": "k@example.com" },
              "lines": [ { "type": "Labour", "description": "{{Text("lines[0].description")}}", "quantity": 1, "unit": "h", "unitPrice": 1, "vatRate": 0.25 } ] }
            """);

        var problem = await ShouldBeProblemAsync(response, HttpStatusCode.BadRequest);
        problem.GetProperty("errors").TryGetProperty(field, out _).ShouldBeTrue();
    }

    [Theory]
    [InlineData("Labour, Material")] // JsonStringEnumConverter alone would store this as Material
    [InlineData("1")]
    public async Task Line_type_must_be_exactly_one_member_name(string type)
    {
        var response = await PostRawJsonAsync("/api/quotes", $$"""
            { "title": "x", "customer": {{ValidCustomer}},
              "lines": [ { "type": "{{type}}", "description": "Work", "quantity": 1, "unit": "h", "unitPrice": 1, "vatRate": 0.25 } ] }
            """);

        var problem = await ShouldBeProblemAsync(response, HttpStatusCode.BadRequest);
        problem.GetProperty("errors").TryGetProperty("lines[0].type", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task Unknown_request_charset_is_415_not_500()
    {
        var content = new StringContent("""{ "title": "x" }""", System.Text.Encoding.UTF8);
        content.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse("application/json; charset=bogus");

        var response = await Client.PostAsync("/api/quotes", content, Ct);

        await ShouldBeProblemAsync(response, HttpStatusCode.UnsupportedMediaType);
    }

    [Fact]
    public async Task Errors_are_ProblemDetails_even_when_the_client_accepts_only_html()
    {
        var sent = await CreateSentAsync();

        var conflict = await SendWithAcceptHtmlAsync(HttpMethod.Put, $"/api/quotes/{sent.Id}", Content());
        var notFound = await SendWithAcceptHtmlAsync(HttpMethod.Get, $"/api/quotes/{Guid.CreateVersion7()}");

        (await ShouldBeProblemAsync(conflict, HttpStatusCode.Conflict))
            .GetProperty("errorCode").GetString().ShouldBe("quote.not_editable");
        await ShouldBeProblemAsync(notFound, HttpStatusCode.NotFound);
    }

    private Task<HttpResponseMessage> SendWithAcceptHtmlAsync(HttpMethod method, string uri, object? body = null)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Accept.ParseAdd("text/html");
        request.Headers.TryAddWithoutValidation("If-Match", "\"2\"");
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return Client.SendAsync(request, Ct);
    }
}
