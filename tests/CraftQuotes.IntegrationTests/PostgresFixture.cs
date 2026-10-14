using CraftQuotes.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace CraftQuotes.IntegrationTests;

/// <summary>One real Postgres container (same image as compose) shared by the whole serialized collection.</summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public string ConnectionString => _container.GetConnectionString();

    public QuotesDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<QuotesDbContext>();
        QuotesDbContext.Configure(options, ConnectionString);
        return new QuotesDbContext(options.Options);
    }

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();
    }

    public ValueTask DisposeAsync() => _container.DisposeAsync();
}

[CollectionDefinition(Name)]
public sealed class PostgresCollectionDefinition : ICollectionFixture<PostgresFixture>
{
    public const string Name = "Postgres";
}
