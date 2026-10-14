using System.Net;
using Shouldly;

namespace CraftQuotes.IntegrationTests;

/// <summary>PLAN §4 conditional requests, exercised on PUT and on a transition.</summary>
public sealed class IfMatchTests(ApiFactory api) : ApiTest(api)
{
    [Theory]
    [InlineData(null, HttpStatusCode.PreconditionRequired)]          // missing
    [InlineData("*", HttpStatusCode.PreconditionRequired)]           // existence only, no revision
    [InlineData("\"2\"", HttpStatusCode.PreconditionFailed)]         // stale / unknown version
    [InlineData("W/\"1\"", HttpStatusCode.PreconditionFailed)]       // weak never matches
    [InlineData("garbage, \"1\"", HttpStatusCode.BadRequest)]        // strict parsing: whole list rejected
    [InlineData("*, \"1\"", HttpStatusCode.BadRequest)]              // * mixed with tags
    [InlineData("1", HttpStatusCode.BadRequest)]                     // unquoted
    public async Task PUT_precondition_failures(string? ifMatch, HttpStatusCode expected)
    {
        var quote = await CreateAsync(); // version 1

        var response = await SendAsync(HttpMethod.Put, $"/api/quotes/{quote.Id}", ifMatch, Content(title: "Changed"));

        await ShouldBeProblemAsync(response, expected);
        var current = await ReadQuoteAsync(await Client.GetAsync($"/api/quotes/{quote.Id}", Ct));
        (current.Title, current.Version).ShouldBe(("EV charger installation", 1));
    }

    [Theory]
    [InlineData("\"1\"")]
    [InlineData("W/\"1\", \"1\"")]   // a weak member doesn't invalidate the strong one
    [InlineData("\"7\", \"1\"")]     // any strong member may match
    public async Task PUT_succeeds_when_a_strong_member_matches(string ifMatch)
    {
        var quote = await CreateAsync();

        var response = await SendAsync(HttpMethod.Put, $"/api/quotes/{quote.Id}", ifMatch, Content(title: "Changed"));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ReadQuoteAsync(response)).Version.ShouldBe(2);
    }

    [Fact]
    public async Task Malformed_If_Match_is_400_even_for_an_unknown_quote()
    {
        var response = await SendAsync(HttpMethod.Post, $"/api/quotes/{Guid.CreateVersion7()}/send", "garbage");

        await ShouldBeProblemAsync(response, HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("send")]
    [InlineData("accept")]
    [InlineData("reject")]
    public async Task Transitions_require_If_Match(string transition)
    {
        var quote = transition == "send" ? await CreateAsync() : await CreateSentAsync();

        var response = await SendAsync(HttpMethod.Post, $"/api/quotes/{quote.Id}/{transition}");

        await ShouldBeProblemAsync(response, HttpStatusCode.PreconditionRequired);
    }
}
