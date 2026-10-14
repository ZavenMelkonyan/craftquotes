namespace CraftQuotes.Api.Domain;

/// <summary>One clock reading: the UTC timestamp and the business date derived from that same instant.</summary>
public readonly record struct BusinessInstant(DateTimeOffset Utc, DateOnly Today);

public sealed class BusinessClock(TimeProvider timeProvider, TimeZoneInfo businessTimeZone)
{
    public BusinessInstant Now()
    {
        var utc = timeProvider.GetUtcNow();
        var local = TimeZoneInfo.ConvertTime(utc, businessTimeZone);
        return new BusinessInstant(utc, DateOnly.FromDateTime(local.DateTime));
    }
}
