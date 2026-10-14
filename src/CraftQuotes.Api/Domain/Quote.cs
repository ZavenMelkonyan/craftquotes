namespace CraftQuotes.Api.Domain;

public sealed class Quote
{
    public const int TitleMaxLength = 200;
    public const int DescriptionMaxLength = 2000;
    public const int RejectionReasonMaxLength = 500;
    public const int MaxLines = 100;

    private readonly List<QuoteLine> _lines = [];

    private Quote() { } // EF Core

    public Guid Id { get; private set; }

    /// <summary>Human-facing sequence number, assigned by the database on insert (0 until saved).</summary>
    public long Number { get; private set; }

    public string Title { get; private set; } = null!;
    public string? Description { get; private set; }
    public CustomerInfo Customer { get; private set; } = null!;
    public DateOnly ValidUntil { get; private set; }
    public QuoteStatus Status { get; private set; }
    public IReadOnlyList<QuoteLine> Lines => _lines.AsReadOnly();
    public decimal Subtotal { get; private set; }
    public decimal VatTotal { get; private set; }
    public decimal Total { get; private set; }
    public string? RejectionReason { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? SentAt { get; private set; }
    public DateTimeOffset? DecidedAt { get; private set; }
    public int Version { get; private set; }

    /// <summary>R1: a new quote is always a draft at version 1.</summary>
    public static Quote CreateDraft(
        string title,
        string? description,
        CustomerInfo customer,
        DateOnly validUntil,
        IReadOnlyList<QuoteLineInput> lines,
        BusinessInstant now)
    {
        var quote = new Quote
        {
            Id = Guid.CreateVersion7(now.Utc),
            Status = QuoteStatus.Draft,
            Version = 1,
            CreatedAt = now.Utc,
            UpdatedAt = now.Utc,
        };
        quote.SetContent(title, description, customer, validUntil, BuildLines(lines));
        return quote;
    }

    /// <summary>R2: full replacement of the editable content; only allowed while Draft.</summary>
    public void ReplaceDraftContent(
        string title,
        string? description,
        CustomerInfo customer,
        DateOnly validUntil,
        IReadOnlyList<QuoteLineInput> lines,
        BusinessInstant now)
    {
        if (Status != QuoteStatus.Draft)
        {
            throw new DomainException(QuoteErrors.NotEditable, $"A {Status} quote cannot be edited.");
        }

        SetContent(title, description, customer, validUntil, BuildLines(lines));
        Touch(now);
    }

    public void Send(BusinessInstant now)
    {
        EnsureStatus(QuoteStatus.Draft, "sent");
        if (_lines.Count == 0)
        {
            throw new DomainException(QuoteErrors.NoLines, "A quote needs at least one line to be sent.");
        }

        if (ValidUntil < now.Today)
        {
            throw new DomainException(
                QuoteErrors.ValidUntilInPast, $"ValidUntil {ValidUntil:yyyy-MM-dd} is before today ({now.Today:yyyy-MM-dd}).");
        }

        Status = QuoteStatus.Sent;
        SentAt = now.Utc;
        Touch(now);
    }

    public void Accept(BusinessInstant now)
    {
        EnsureStatus(QuoteStatus.Sent, "accepted");
        if (IsExpired(now.Today))
        {
            throw new DomainException(QuoteErrors.Expired, $"The quote expired after {ValidUntil:yyyy-MM-dd}.");
        }

        Status = QuoteStatus.Accepted;
        DecidedAt = now.Utc;
        Touch(now);
    }

    /// <summary>R6: an expired quote can still be rejected.</summary>
    public void Reject(string? reason, BusinessInstant now)
    {
        EnsureStatus(QuoteStatus.Sent, "rejected");
        Guard.Optional(reason, RejectionReasonMaxLength);

        Status = QuoteStatus.Rejected;
        RejectionReason = reason;
        DecidedAt = now.Utc;
        Touch(now);
    }

    /// <summary>Derived, never stored: only a sent quote can expire, and <see cref="ValidUntil"/> is inclusive.</summary>
    public bool IsExpired(DateOnly today) => Status == QuoteStatus.Sent && today > ValidUntil;

    private static List<QuoteLine> BuildLines(IReadOnlyList<QuoteLineInput> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(lines.Count, MaxLines);
        return lines.Select((line, index) => QuoteLine.Create(index + 1, line)).ToList();
    }

    // Validates every argument before assigning anything, so a rejected command never leaves a half-applied change.
    private void SetContent(
        string title, string? description, CustomerInfo customer, DateOnly validUntil, List<QuoteLine> lines)
    {
        var validTitle = Guard.Required(title, TitleMaxLength);
        var validDescription = Guard.Optional(description, DescriptionMaxLength);
        ArgumentNullException.ThrowIfNull(customer);

        Title = validTitle;
        Description = validDescription;
        Customer = customer;
        ValidUntil = validUntil;

        _lines.Clear();
        _lines.AddRange(lines);
        Subtotal = lines.Sum(l => l.NetAmount);
        VatTotal = lines.Sum(l => l.VatAmount);
        Total = lines.Sum(l => l.TotalAmount);
    }

    private void EnsureStatus(QuoteStatus required, string action)
    {
        if (Status != required)
        {
            throw new DomainException(
                QuoteErrors.InvalidTransition, $"A {Status} quote cannot be {action}; it must be {required}.");
        }
    }

    /// <summary>R10: the single place where a successful mutation bumps the version.</summary>
    private void Touch(BusinessInstant now)
    {
        UpdatedAt = now.Utc;
        Version++;
    }
}
