using CraftQuotes.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace CraftQuotes.Api.Infrastructure.Persistence;

public sealed class QuotesDbContext(DbContextOptions<QuotesDbContext> options) : DbContext(options)
{
    public const string QuoteNumberSequence = "quote_number_seq";

    public DbSet<Quote> Quotes => Set<Quote>();

    /// <summary>Single place for provider + naming setup, shared by the app and the tests.</summary>
    public static DbContextOptionsBuilder Configure(DbContextOptionsBuilder options, string connectionString) =>
        options.UseNpgsql(connectionString).UseSnakeCaseNamingConvention();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasSequence<long>(QuoteNumberSequence);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(QuotesDbContext).Assembly);
    }
}
