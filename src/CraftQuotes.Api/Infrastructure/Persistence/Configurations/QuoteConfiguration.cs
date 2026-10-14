using CraftQuotes.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CraftQuotes.Api.Infrastructure.Persistence.Configurations;

internal sealed class QuoteConfiguration : IEntityTypeConfiguration<Quote>
{
    private const string Amount = "numeric(18,2)";
    private const int EnumMaxLength = 20;

    public void Configure(EntityTypeBuilder<Quote> builder)
    {
        builder.ToTable("quotes");
        builder.HasKey(q => q.Id);
        builder.Property(q => q.Id).ValueGeneratedNever();

        builder.Property(q => q.Number)
            .HasDefaultValueSql($"nextval('{QuotesDbContext.QuoteNumberSequence}')");
        builder.HasIndex(q => q.Number).IsUnique();

        builder.Property(q => q.Title).HasMaxLength(Quote.TitleMaxLength);
        builder.Property(q => q.Description).HasMaxLength(Quote.DescriptionMaxLength);
        builder.Property(q => q.Status).HasConversion<string>().HasMaxLength(EnumMaxLength);
        builder.Property(q => q.RejectionReason).HasMaxLength(Quote.RejectionReasonMaxLength);
        builder.Property(q => q.Subtotal).HasColumnType(Amount);
        builder.Property(q => q.VatTotal).HasColumnType(Amount);
        builder.Property(q => q.Total).HasColumnType(Amount);
        builder.Property(q => q.Version).IsConcurrencyToken();

        builder.HasIndex(q => q.Status);
        builder.HasIndex(q => q.CreatedAt);

        builder.ComplexProperty(q => q.Customer, customer =>
        {
            customer.IsRequired();
            customer.Property(c => c.Name).HasMaxLength(CustomerInfo.NameMaxLength);
            customer.Property(c => c.Email).HasMaxLength(CustomerInfo.EmailMaxLength);
            customer.Property(c => c.Phone).HasMaxLength(CustomerInfo.PhoneMaxLength);
            customer.Property(c => c.SiteAddress).HasMaxLength(CustomerInfo.SiteAddressMaxLength);
        });

        builder.OwnsMany(q => q.Lines, line =>
        {
            line.ToTable("quote_lines");
            line.WithOwner().HasForeignKey("QuoteId");
            line.Property<int>("Id");
            line.HasKey("Id");
            line.HasIndex("QuoteId", nameof(QuoteLine.Position)).IsUnique();

            line.Property(l => l.Type).HasConversion<string>().HasMaxLength(EnumMaxLength);
            line.Property(l => l.Description).HasMaxLength(QuoteLine.DescriptionMaxLength);
            line.Property(l => l.Unit).HasMaxLength(QuoteLine.UnitMaxLength);
            line.Property(l => l.Quantity).HasColumnType("numeric(18,3)");
            line.Property(l => l.UnitPrice).HasColumnType(Amount);
            line.Property(l => l.VatRate).HasColumnType("numeric(5,4)");
            line.Property(l => l.NetAmount).HasColumnType(Amount);
            line.Property(l => l.VatAmount).HasColumnType(Amount);
            line.Property(l => l.TotalAmount).HasColumnType(Amount);
        });
        builder.Navigation(q => q.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
