using CraftQuotes.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Resolved lazily so test hosts can override the connection string after Program starts.
builder.Services.AddDbContext<QuotesDbContext>((services, options) => QuotesDbContext.Configure(
    options,
    services.GetRequiredService<IConfiguration>().GetConnectionString("Quotes")
        ?? throw new InvalidOperationException("Connection string 'Quotes' is not configured.")));

var app = builder.Build();

// Convenience for local/compose runs only; production would apply a migration bundle in the pipeline.
if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<QuotesDbContext>().Database.MigrateAsync();
}

await app.RunAsync();
