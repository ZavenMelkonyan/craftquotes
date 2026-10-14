using System.Text.Json.Serialization;
using CraftQuotes.Api.Domain;
using CraftQuotes.Api.Features.Quotes;
using CraftQuotes.Api.Infrastructure.Http;
using CraftQuotes.Api.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Resolved lazily so test hosts can override the connection string after Program starts.
builder.Services.AddDbContext<QuotesDbContext>((services, options) => QuotesDbContext.Configure(
    options,
    services.GetRequiredService<IConfiguration>().GetConnectionString("Quotes")
        ?? throw new InvalidOperationException("Connection string 'Quotes' is not configured.")));

builder.Services.AddOptions<QuotesOptions>().BindConfiguration(QuotesOptions.SectionName);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(services => new BusinessClock(
    services.GetRequiredService<TimeProvider>(),
    TimeZoneInfo.FindSystemTimeZoneById(services.GetRequiredService<IOptions<QuotesOptions>>().Value.BusinessTimeZone)));

builder.Services.AddValidatorsFromAssemblyContaining<Program>(includeInternalTypes: true);
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false)));

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddOpenApi(options => options.AddOperationTransformer<IfMatchOperationTransformer>());
builder.Services.AddHealthChecks().AddDbContextCheck<QuotesDbContext>(tags: ["ready"]);

if (!builder.Environment.IsDevelopment())
{
    builder.Logging.ClearProviders();
    builder.Logging.AddJsonConsole();
}

var app = builder.Build();

// Convenience for local/compose runs only; production would apply a migration bundle in the pipeline.
if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<QuotesDbContext>().Database.MigrateAsync();
}

app.UseExceptionHandler();
app.UseStatusCodePages();

// Gated by config, not environment name, so the compose container can expose the UI too.
if (app.Configuration.GetValue<bool>("OpenApi:EnableUi"))
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.MapQuoteEndpoints();

// Liveness checks nothing external (a DB outage must not restart the process); readiness checks the database.
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

await app.RunAsync();
