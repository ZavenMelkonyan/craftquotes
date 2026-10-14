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
}
