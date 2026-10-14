namespace CraftQuotes.Api.Domain;

/// <summary>Snapshot of the customer as quoted; deliberately not a reference to a live customer record.</summary>
public sealed record CustomerInfo
{
    public const int NameMaxLength = 200;
    public const int EmailMaxLength = 320;
    public const int PhoneMaxLength = 50;
    public const int SiteAddressMaxLength = 300;

    public CustomerInfo(string name, string email, string? phone, string? siteAddress)
    {
        Name = Guard.Required(name, NameMaxLength);
        Email = Guard.Required(email, EmailMaxLength);
        Phone = Guard.Optional(phone, PhoneMaxLength);
        SiteAddress = Guard.Optional(siteAddress, SiteAddressMaxLength);
    }

    public string Name { get; }
    public string Email { get; }
    public string? Phone { get; }
    public string? SiteAddress { get; }
}
