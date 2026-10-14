using System.Net.Http.Json;
using System.Text.Json;
using Shouldly;

namespace CraftQuotes.IntegrationTests;

/// <summary>The published contract must match what the API accepts (UI is enabled in Development, as in compose).</summary>
public sealed class OpenApiTests(ApiFactory api) : ApiTest(api)
{
    [Fact]
    public async Task Only_truly_required_members_are_documented_as_required()
    {
        var schemas = (await Document()).GetProperty("components").GetProperty("schemas");

        Required(schemas, "CreateQuoteRequest").ShouldBe(["title", "customer"], ignoreOrder: true);
        Required(schemas, "UpdateQuoteRequest").ShouldBe(["title", "customer", "validUntil", "lines"], ignoreOrder: true);
        Required(schemas, "CustomerRequest").ShouldBe(["name", "email"], ignoreOrder: true);
        Required(schemas, "RejectQuoteRequest").ShouldBeEmpty();
    }

    [Fact]
    public async Task List_query_parameters_are_camelCase()
    {
        var parameters = (await Document()).GetProperty("paths").GetProperty("/api/quotes").GetProperty("get")
            .GetProperty("parameters").EnumerateArray().Select(p => p.GetProperty("name").GetString());

        parameters.ShouldBe(["status", "search", "page", "pageSize"], ignoreOrder: true);
    }

    private async Task<JsonElement> Document() =>
        await Client.GetFromJsonAsync<JsonElement>("/openapi/v1.json", Ct);

    private static string?[] Required(JsonElement schemas, string name) =>
        schemas.GetProperty(name).TryGetProperty("required", out var required)
            ? required.EnumerateArray().Select(r => r.GetString()).ToArray()
            : [];
}
