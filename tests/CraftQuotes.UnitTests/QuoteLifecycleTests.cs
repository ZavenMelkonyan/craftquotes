using CraftQuotes.Api.Domain;
using Shouldly;
using static CraftQuotes.UnitTests.TestData;

namespace CraftQuotes.UnitTests;

public sealed class QuoteLifecycleTests
{
    [Fact]
    public void R1_New_quote_is_draft_with_version_1()
    {
        var quote = Draft();

        quote.Status.ShouldBe(QuoteStatus.Draft);
        quote.Version.ShouldBe(1);
        quote.CreatedAt.ShouldBe(Now.Utc);
        quote.UpdatedAt.ShouldBe(Now.Utc);
        quote.SentAt.ShouldBeNull();
        quote.DecidedAt.ShouldBeNull();
    }

    [Fact]
    public void R2_Draft_content_is_replaced_in_full_and_lines_follow_request_order()
    {
        var quote = Draft();
        var later = At(Today.AddDays(1));
        var customer = new CustomerInfo("Kari Nordmann", "kari@example.no", "+47 900 00 000", null);

        quote.ReplaceDraftContent(
            "EV charger + RCD",
            null,
            customer,
            Today.AddDays(10),
            [Line(quantity: 2m, unitPrice: 10m), Line(quantity: 1m, unitPrice: 5m)],
            later);

        quote.Title.ShouldBe("EV charger + RCD");
        quote.Customer.ShouldBe(customer);
        quote.ValidUntil.ShouldBe(Today.AddDays(10));
        quote.Lines.Select(l => (l.Position, l.NetAmount)).ShouldBe([(1, 20.00m), (2, 5.00m)]);
        quote.Subtotal.ShouldBe(25.00m);
        quote.Version.ShouldBe(2);
        quote.UpdatedAt.ShouldBe(later.Utc);
    }

    [Fact]
    public void R2_Cannot_edit_sent_quote()
    {
        var quote = Sent();

        var error = Should.Throw<DomainException>(
            () => quote.ReplaceDraftContent("New", null, Kari, Today.AddDays(30), KariLines, Now));

        error.Code.ShouldBe(QuoteErrors.NotEditable);
    }

    [Fact]
    public void R3_Draft_may_have_zero_lines()
    {
        var quote = Draft(lines: []);

        quote.Lines.ShouldBeEmpty();
        quote.Total.ShouldBe(0m);
    }

    [Fact]
    public void R3_Sending_requires_at_least_one_line()
    {
        var quote = Draft(lines: []);

        Should.Throw<DomainException>(() => quote.Send(Now)).Code.ShouldBe(QuoteErrors.NoLines);
    }

    [Fact]
    public void R4_Cannot_send_when_valid_until_is_before_today()
    {
        var quote = Draft(validUntil: Today.AddDays(-1));

        Should.Throw<DomainException>(() => quote.Send(Now)).Code.ShouldBe(QuoteErrors.ValidUntilInPast);
    }

    [Fact]
    public void R4_Can_send_on_the_last_valid_day()
    {
        var quote = Draft(validUntil: Today);

        quote.Send(Now);

        quote.Status.ShouldBe(QuoteStatus.Sent);
        quote.SentAt.ShouldBe(Now.Utc);
        quote.Version.ShouldBe(2);
    }

    [Fact]
    public void R5_Draft_cannot_be_accepted_or_rejected()
    {
        var quote = Draft();

        Should.Throw<DomainException>(() => quote.Accept(Now)).Code.ShouldBe(QuoteErrors.InvalidTransition);
        Should.Throw<DomainException>(() => quote.Reject(null, Now)).Code.ShouldBe(QuoteErrors.InvalidTransition);
    }

    [Fact]
    public void R5_Sent_quote_can_be_accepted()
    {
        var quote = Sent();

        quote.Accept(Now);

        quote.Status.ShouldBe(QuoteStatus.Accepted);
        quote.DecidedAt.ShouldBe(Now.Utc);
        quote.Version.ShouldBe(3);
    }

    [Fact]
    public void R5_Sent_quote_can_be_rejected_with_reason()
    {
        var quote = Sent();

        quote.Reject("Too expensive", Now);

        quote.Status.ShouldBe(QuoteStatus.Rejected);
        quote.RejectionReason.ShouldBe("Too expensive");
        quote.DecidedAt.ShouldBe(Now.Utc);
        quote.Version.ShouldBe(3);
    }

    [Fact]
    public void R5_Sent_quote_can_be_rejected_without_reason()
    {
        var quote = Sent();

        quote.Reject(null, Now);

        quote.Status.ShouldBe(QuoteStatus.Rejected);
        quote.RejectionReason.ShouldBeNull();
    }

    [Fact]
    public void R6_Expired_quote_cannot_be_accepted()
    {
        var quote = Sent(validUntil: Today);

        Should.Throw<DomainException>(() => quote.Accept(At(Today.AddDays(1)))).Code.ShouldBe(QuoteErrors.Expired);
    }

    [Fact]
    public void R6_Quote_can_be_accepted_on_the_last_valid_day()
    {
        var quote = Sent(validUntil: Today);

        quote.Accept(At(Today));

        quote.Status.ShouldBe(QuoteStatus.Accepted);
    }

    [Fact]
    public void R6_Expired_quote_can_still_be_rejected()
    {
        var quote = Sent(validUntil: Today);

        quote.Reject("Too late", At(Today.AddDays(1)));

        quote.Status.ShouldBe(QuoteStatus.Rejected);
    }

    [Fact]
    public void R6_Only_a_sent_quote_is_ever_expired()
    {
        var afterExpiry = Today.AddDays(31);

        Draft().IsExpired(afterExpiry).ShouldBeFalse();
        Sent().IsExpired(afterExpiry).ShouldBeTrue();
        Accepted().IsExpired(afterExpiry).ShouldBeFalse();
        Rejected().IsExpired(afterExpiry).ShouldBeFalse();
    }

    [Theory]
    [InlineData(QuoteStatus.Accepted)]
    [InlineData(QuoteStatus.Rejected)]
    public void R7_Terminal_quote_accepts_no_further_changes(QuoteStatus terminal)
    {
        var quote = terminal == QuoteStatus.Accepted ? Accepted() : Rejected();

        Should.Throw<DomainException>(
                () => quote.ReplaceDraftContent("New", null, Kari, Today.AddDays(30), KariLines, Now))
            .Code.ShouldBe(QuoteErrors.NotEditable);
        Should.Throw<DomainException>(() => quote.Send(Now)).Code.ShouldBe(QuoteErrors.InvalidTransition);
        Should.Throw<DomainException>(() => quote.Accept(Now)).Code.ShouldBe(QuoteErrors.InvalidTransition);
        Should.Throw<DomainException>(() => quote.Reject(null, Now)).Code.ShouldBe(QuoteErrors.InvalidTransition);
        quote.Version.ShouldBe(3);
    }

    [Fact]
    public void R10_Every_successful_mutation_increments_version_and_updates_timestamp()
    {
        var quote = Draft();
        var versions = new List<int> { quote.Version };

        quote.ReplaceDraftContent("Edited", null, Kari, Today.AddDays(30), KariLines, At(Today.AddDays(1)));
        versions.Add(quote.Version);
        quote.Send(At(Today.AddDays(2)));
        versions.Add(quote.Version);
        quote.Accept(At(Today.AddDays(3)));
        versions.Add(quote.Version);

        versions.ShouldBe([1, 2, 3, 4]);
        quote.UpdatedAt.ShouldBe(At(Today.AddDays(3)).Utc);
        quote.CreatedAt.ShouldBe(Now.Utc);
    }

    [Fact]
    public void R10_Failed_transition_leaves_quote_unchanged()
    {
        var quote = Draft(lines: []);

        Should.Throw<DomainException>(() => quote.Send(At(Today.AddDays(1))));

        quote.Status.ShouldBe(QuoteStatus.Draft);
        quote.Version.ShouldBe(1);
        quote.UpdatedAt.ShouldBe(Now.Utc);
        quote.SentAt.ShouldBeNull();
    }

    [Fact]
    public void R10_Failed_edit_with_one_invalid_line_leaves_quote_unchanged()
    {
        var quote = Draft();
        var totalBefore = quote.Total;

        Should.Throw<ArgumentException>(() => quote.ReplaceDraftContent(
            "Edited",
            "Should not be applied",
            Kari,
            Today.AddDays(5),
            [Line(), Line(quantity: 1.0001m)],
            At(Today.AddDays(1))));

        quote.Title.ShouldBe("EV charger installation");
        quote.Description.ShouldBeNull();
        quote.ValidUntil.ShouldBe(Today.AddDays(30));
        quote.Lines.Count.ShouldBe(5);
        quote.Total.ShouldBe(totalBefore);
        quote.Version.ShouldBe(1);
        quote.UpdatedAt.ShouldBe(Now.Utc);
    }

    [Fact]
    public void Lines_cannot_be_mutated_through_a_cast()
    {
        var quote = Draft();

        var asList = quote.Lines as IList<QuoteLine>;

        asList.ShouldNotBeNull();
        Should.Throw<NotSupportedException>(() => asList.Clear());
        quote.Lines.Count.ShouldBe(5);
    }
}
