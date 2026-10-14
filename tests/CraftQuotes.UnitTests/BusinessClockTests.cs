using System.Globalization;
using CraftQuotes.Api.Domain;
using Microsoft.Extensions.Time.Testing;
using Shouldly;

namespace CraftQuotes.UnitTests;

public sealed class BusinessClockTests
{
    private static readonly TimeZoneInfo Oslo = TimeZoneInfo.FindSystemTimeZoneById("Europe/Oslo");

    [Theory]
    // Winter (CET, UTC+1): local midnight is 23:00 UTC.
    [InlineData("2026-01-15T22:59:59Z", "2026-01-15")]
    [InlineData("2026-01-15T23:00:00Z", "2026-01-16")]
    // Summer (CEST, UTC+2): local midnight is 22:00 UTC.
    [InlineData("2026-07-15T21:59:59Z", "2026-07-15")]
    [InlineData("2026-07-15T22:00:00Z", "2026-07-16")]
    public void Business_date_is_the_Oslo_date_of_the_instant(string utc, string expectedToday)
    {
        var now = ClockAt(Utc(utc)).Now();

        now.Today.ShouldBe(DateOnly.Parse(expectedToday, CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData("2026-01-15T22:59:59Z", false)]
    [InlineData("2026-01-15T23:00:00Z", true)]
    [InlineData("2026-07-15T21:59:59Z", false)]
    [InlineData("2026-07-15T22:00:00Z", true)]
    public void R6_Quote_expires_at_Oslo_midnight_after_valid_until_in_winter_and_summer(string utc, bool expired)
    {
        var validUntil = DateOnly.FromDateTime(Utc(utc).UtcDateTime);
        var quote = TestData.Draft(validUntil: validUntil);
        quote.Send(TestData.At(validUntil));

        quote.IsExpired(ClockAt(Utc(utc)).Now().Today).ShouldBe(expired);
    }

    [Fact]
    public void Timestamp_and_business_date_come_from_one_reading()
    {
        // 23:30 in Oslo; a second read one hour later would already be the next business date.
        var time = new FakeTimeProvider(Utc("2026-01-15T22:30:00Z")) { AutoAdvanceAmount = TimeSpan.FromHours(1) };

        var now = new BusinessClock(time, Oslo).Now();

        now.Utc.ShouldBe(Utc("2026-01-15T22:30:00Z"));
        now.Utc.Offset.ShouldBe(TimeSpan.Zero);
        now.Today.ShouldBe(new DateOnly(2026, 1, 15));
    }

    private static BusinessClock ClockAt(DateTimeOffset utc) => new(new FakeTimeProvider(utc), Oslo);

    private static DateTimeOffset Utc(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture);
}
