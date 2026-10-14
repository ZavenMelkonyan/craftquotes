using CraftQuotes.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Testcontainers.PostgreSql;

namespace CraftQuotes.IntegrationTests;

/// <summary>
/// The API on a real Postgres container (same image as compose), shared by one serialized test collection.
/// The container starts before the host, so startup migrations run against it.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    /// <summary>Every test starts at this instant: 12:00 in Oslo on 2026-10-14.</summary>
    public static readonly DateTimeOffset StartTime = new(2026, 10, 14, 10, 0, 0, TimeSpan.Zero);

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public FakeTimeProvider Time { get; } = new(StartTime);

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();
        _ = Services; // build the host now: runs the startup migration once for the collection
    }

    /// <summary>Per-test isolation: empty tables and the clock back at <see cref="StartTime"/>.</summary>
    public async Task ResetAsync()
    {
        await using var db = CreateDbContext();
        await db.Database.ExecuteSqlRawAsync("TRUNCATE quotes, quote_lines RESTART IDENTITY");
        Time.AdjustTime(StartTime); // SetUtcNow can't move backwards
    }

    /// <summary>An independent context, as a second process or request would have.</summary>
    public QuotesDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<QuotesDbContext>();
        QuotesDbContext.Configure(options, _postgres.GetConnectionString());
        return new QuotesDbContext(options.Options);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Quotes", _postgres.GetConnectionString());
        builder.UseSetting("Database:MigrateOnStartup", "true");
        builder.ConfigureServices(services => services.AddSingleton<TimeProvider>(Time));
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollectionDefinition : ICollectionFixture<ApiFactory>
{
    public const string Name = "Api";
}
