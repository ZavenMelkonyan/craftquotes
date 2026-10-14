using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CraftQuotes.Api.Features.Quotes;
using Shouldly;

namespace CraftQuotes.IntegrationTests;

/// <summary>Base for tests in the shared collection: resets state before each test and offers HTTP helpers.</summary>
[Collection(ApiCollectionDefinition.Name)]
public abstract class ApiTest(ApiFactory api) : IAsyncLifetime
{
    protected static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    protected ApiFactory Api { get; } = api;
    protected HttpClient Client { get; } = api.CreateClient();
    protected static CancellationToken Ct => TestContext.Current.CancellationToken;
    protected static DateOnly Today => DateOnly.FromDateTime(ApiFactory.StartTime.UtcDateTime);

    public async ValueTask InitializeAsync() => await Api.ResetAsync();

    public ValueTask DisposeAsync()
    {
        Client.Dispose();
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }

    protected static object Line(
        string description = "Installation, electrician", decimal quantity = 5, decimal unitPrice = 950, decimal vatRate = 0.25m) =>
        new { type = "Labour", description, quantity, unit = "h", unitPrice, vatRate };

    protected static object[] KariLines =>
    [
        new { type = "Labour", description = "Installation, electrician", quantity = 5, unit = "h", unitPrice = 950, vatRate = 0.25 },
        new { type = "Material", description = "EV charger 11 kW", quantity = 1, unit = "pcs", unitPrice = 8990, vatRate = 0.25 },
        new { type = "Material", description = "Cable 5x6 mm²", quantity = 15, unit = "m", unitPrice = 89, vatRate = 0.25 },
        new { type = "Material", description = "RCD Type B 40A", quantity = 1, unit = "pcs", unitPrice = 2450, vatRate = 0.25 },
        new { type = "Other", description = "Call-out / travel", quantity = 1, unit = "pcs", unitPrice = 690, vatRate = 0.25 },
    ];

    protected static object Content(
        string title = "EV charger installation", object[]? lines = null, DateOnly? validUntil = null, string customerName = "Kari Nordmann") =>
        new
        {
            title,
            customer = new { name = customerName, email = "kari@example.com", siteAddress = "Fjellveien 12, 5019 Bergen" },
            validUntil = validUntil ?? Today.AddDays(30),
            lines = lines ?? KariLines,
        };

    protected async Task<QuoteResponse> CreateAsync(object? body = null)
    {
        var response = await Client.PostAsJsonAsync("/api/quotes", body ?? Content(), Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<QuoteResponse>(Json, Ct))!;
    }

    protected async Task<QuoteResponse> CreateSentAsync(DateOnly? validUntil = null)
    {
        var quote = await CreateAsync(Content(validUntil: validUntil));
        var sent = await SendAsync(HttpMethod.Post, $"/api/quotes/{quote.Id}/send", ifMatch: "\"1\"");
        sent.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await sent.Content.ReadFromJsonAsync<QuoteResponse>(Json, Ct))!;
    }

    protected Task<HttpResponseMessage> SendAsync(HttpMethod method, string uri, string? ifMatch = null, object? body = null)
    {
        var request = new HttpRequestMessage(method, uri);
        if (ifMatch is not null)
        {
            request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: Json);
        }

        return Client.SendAsync(request, Ct);
    }

    protected Task<HttpResponseMessage> PostRawJsonAsync(string uri, string json) =>
        Client.PostAsync(uri, new StringContent(json, Encoding.UTF8, "application/json"), Ct);

    protected static async Task<QuoteResponse> ReadQuoteAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<QuoteResponse>(Json, Ct))!;

    /// <summary>Asserts a ProblemDetails response with the given status and returns its JSON for further checks.</summary>
    protected static async Task<JsonElement> ShouldBeProblemAsync(HttpResponseMessage response, HttpStatusCode status)
    {
        response.StatusCode.ShouldBe(status);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        problem.GetProperty("status").GetInt32().ShouldBe((int)status);
        return problem;
    }
}
