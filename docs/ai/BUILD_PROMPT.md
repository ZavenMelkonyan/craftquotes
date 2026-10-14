# Build Prompt — CraftQuotes

> Paste this into a fresh Claude Code session opened in the repository root.

---

You are pairing with me, a **senior/lead .NET developer**, on a technical interview assignment. In the follow-up interview I will walk the panel through **every line** and defend every decision. Code must therefore be clear, idiomatic, minimal and defensible: no cleverness, no speculative abstractions, no AI padding.

## Source of truth

1. Read `docs/ai/PLAN.md` **fully** before doing anything. It defines:
   - the domain and business rules **R1–R11**;
   - the API contract, including conditional requests, error precedence and PUT semantics (§4);
   - decisions **D1–D19**;
   - testing strategy (§7);
   - phases **P0–P6**, the checkpoint and the cut list (§8).
2. Read `docs/ai/AI_LOG.md`. It already has a planning entry; append to it.
3. **Ask me** about contract or domain ambiguities, or anything that contradicts PLAN.md.
   **Don't ask** about routine implementation choices (naming, file split, helper shape). Decide, then record it in AI_LOG under "Decisions made by AI".
4. The original assignment: build a small .NET service exposing an API (create, retrieve, update) that persists data in a database; 3–4 hour limit; README covering how to run, key decisions/trade-offs/limitations, and how AI was used and verified.

## Working agreement (non-negotiable)

1. **Work phase by phase** (P0 → P6). At the end of each phase:
   - run `dotnet build -warnaserror` and `dotnet test`, and show me the **actual** result summary (counts, failures);
   - give me a short review brief: what changed, **which files to review first**, decisions you made, anything you're unsure about;
   - report **elapsed time** since the P0 start timestamp (recorded in AI_LOG; use the system clock) and the remaining budget;
   - append an entry to `docs/ai/AI_LOG.md` (template below);
   - create a local commit with a Conventional Commit message (`chore:`, `feat:`, `test:`, `docs:`, `fix:`);
   - **stop and wait** until I reply "continue".
2. **Checkpoint:** once elapsed time passes **3h15**, stop at the next gate. List the remaining work and the cut list (PLAN §8). **Never cut scope on your own.**
   If I approve a cut, update the README, API table and definition of done in the same commit.
3. **Timebox optional tooling.** If an optional or tooling problem (sequence numbering, Scalar, CI, Docker details) costs more than **15 minutes**, stop and tell me instead of fighting it.
4. **Never claim something works without running it.** If something fails, show the error and fix the root cause.
   - Never suppress warnings, skip or ignore tests, weaken assertions, or catch-and-swallow to get green.
   - After any deliberate break (mutation check), show a green re-run.
5. **Allowed NuGet packages only** (list below). Anything else requires asking me first.
6. **Forbidden without my approval:** MediatR, AutoMapper/Mapster, repository or unit-of-work abstractions over EF Core, generic base entity/repository classes, CQRS project splits, extra projects beyond the three listed, Serilog, any `Result<T>` library.
7. Use current stable **.NET 10 / C# 14 / EF Core 10** APIs. If you're unsure an API exists or behaves as you think, verify by compiling or with a tiny experiment, not from memory.
   Example: check the current `PostgreSqlBuilder` constructor shape in the installed Testcontainers version.
8. Comments explain **why**, not what. Keep them sparse.
9. **Do not push** to any remote. I will push.
   Be precise about CI: "workflow written / YAML valid locally" is not "CI passed". Only remote runs prove the latter.
10. Keep diffs focused on the current phase. No drive-by refactors of earlier phases unless needed (and then say so).

## Tech stack

- SDK: .NET 10 (`global.json`: `10.0.100`, `rollForward: latestFeature`). Target `net10.0`. **Don't set `LangVersion`**; use the SDK default (C# 14).
- Solution file: `CraftQuotes.slnx`.
- Projects: `src/CraftQuotes.Api`, `tests/CraftQuotes.UnitTests`, `tests/CraftQuotes.IntegrationTests`.
- PostgreSQL `postgres:17-alpine` in `docker-compose.yml`.
- `dotnet-ef` installed as a **local tool** (`dotnet new tool-manifest` + `dotnet tool install dotnet-ef`), on the same major version as EF Core.

### Allowed packages
Pin **exact** versions in `Directory.Packages.props`. Keep ASP.NET Core, EF Core, Npgsql provider and `EFCore.NamingConventions` on a coherent **10.x** set. Verify restore + build in P0.

- **API:**
  - `Microsoft.AspNetCore.OpenApi`
  - `Scalar.AspNetCore`
  - `Npgsql.EntityFrameworkCore.PostgreSQL`
  - `Microsoft.EntityFrameworkCore.Design`
  - `EFCore.NamingConventions`
  - `FluentValidation`
  - `FluentValidation.DependencyInjectionExtensions`
  - `Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore`
- **Tests:**
  - `xunit.v3`
  - `xunit.runner.visualstudio`
  - `Microsoft.NET.Test.Sdk` (VSTest mode; don't mix runner modes)
  - `Shouldly`
  - `Microsoft.AspNetCore.Mvc.Testing`
  - `Testcontainers.PostgreSql`
  - `Microsoft.Extensions.TimeProvider.Testing`
  - Note: xUnit v3 lifecycle methods (`IAsyncLifetime`) return `ValueTask`.

## Code conventions

**Build and style**
- `Directory.Build.props`: `Nullable=enable`, `ImplicitUsings=enable`, `TreatWarningsAsErrors=true`, `AnalysisLevel=latest-recommended`. Central package management via `Directory.Packages.props`.
- File-scoped namespaces; `sealed` by default; records for DTOs.

**Domain**
- **Domain encapsulation:**
  - `Quote.Lines` is `IReadOnlyList<QuoteLine>` returning a read-only wrapper (`_lines.AsReadOnly()`), never the backing list itself. EF maps the private field;
  - `QuoteLine` and `CustomerInfo` are immutable;
  - no public setters;
  - commands validate everything first, then mutate (a failed command leaves state and `Version` unchanged).
- Domain has **no** dependency on ASP.NET Core or EF Core. EF mapping lives in `IEntityTypeConfiguration<T>` classes; private constructors and fields are fine.

**Endpoints and data access**
- One file per use case in `Features/Quotes/` (request record + validator + handler method). `QuoteEndpoints.cs` maps the route group. Map to `QuoteResponse` explicitly, by hand; lines ordered by `Position`.
- `CancellationToken` flows through every async call. Reads use `AsNoTracking()`. The list endpoint projects to the summary DTO in SQL.

**Time and money**
- Inject `TimeProvider`. Never use `DateTime.Now/UtcNow`.
  - Each command reads the clock **once**.
  - A small `BusinessClock` derives the UTC timestamp (offset 0, required by Npgsql for `timestamptz`) and the `Europe/Oslo` business date (`Quotes:BusinessTimeZone`) from that instant.
- Money: one rounding helper (`MidpointRounding.AwayFromZero`, 2 decimals). No `double`/`float` anywhere near money.
- R9 is enforced in **both** the validator (400) and the domain:
  - scale checks reject too many decimals (never round inputs). Check significant decimals via `value == Math.Round(value, n)`; trailing zeros like `950.000` are valid;
  - range checks enforce the bounds.

**JSON, HTTP and errors**
- JSON: camelCase, `JsonStringEnumConverter` with **`allowIntegerValues: false`**, registered via `ConfigureHttpJsonOptions`.
- **HTTP conditions** (PLAN §4, follow its parsing rules and mechanics exactly): parse `If-Match` with `Microsoft.Net.Http.Headers.EntityTagHeaderValue.TryParseStrictList`, **not** `TryParseList` (it silently skips garbage). Put the parsing and matching in one helper.
  - Required on `PUT`, `/send`, `/accept`, `/reject`.
  - Missing or `*` alone → 428; malformed or `*` mixed with tags → 400; no strong member matching → 412.
  - **Order in each mutating handler:**
    1. load (404);
    2. run the domain command (409);
    3. evaluate `If-Match` against the **loaded** version (428/412, nothing saved);
    4. `SaveChanges`.
  - EF's tracked original `Version` already equals the checked version, so don't add code to set it.
  - `DbUpdateConcurrencyException` → 412.
- **ETag** header only on `GET /api/quotes/{id}`. Mutation responses return the body including `version`, with no ETag header (D17).
- **Errors:**
  - wire `AddProblemDetails()`, `UseExceptionHandler()` and `UseStatusCodePages()` explicitly;
  - an `IExceptionHandler` maps `DomainException(code, message)` → 409 with an `errorCode` extension;
  - the same handler maps `BadHttpRequestException` → its own `StatusCode` as ProblemDetails. Minimal APIs **throw** it in Development (`ThrowOnBadRequest`); elsewhere they return an empty 400 that `UseStatusCodePages` fills;
  - validation → 400 `ValidationProblemDetails`;
  - JSON binding failures (malformed JSON, bad enum) must produce a 400 ProblemDetails **in both Development and the container environment**. Verify this; don't assume it.
- Error precedence per PLAN §4.

**Configuration**
- `ConnectionStrings:Quotes`.
- `Database:MigrateOnStartup` (default `false`; `true` in Development and compose).
- `OpenApi:EnableUi` (default `false`; `true` in Development and compose) gates `MapOpenApi` + Scalar, independent of environment name.

## Phases

### P0 — Scaffold
- Record the **start timestamp** in AI_LOG.
- `git init`, then create:
  - `.gitignore` (`dotnet new gitignore`) and a minimal `.editorconfig`;
  - `global.json`, `Directory.Build.props`, `Directory.Packages.props`;
  - the tool manifest with `dotnet-ef`;
  - `CraftQuotes.slnx` with the three projects (test projects reference the API).
- `docker-compose.yml` with a `db` service: healthcheck via `pg_isready`, named volume, dev-only credentials, port 5432.
- `.github/workflows/ci.yml`: `ubuntu-latest`; set up .NET 10; restore; `build -c Release -warnaserror`; `test -c Release`.
- `README.md` skeleton with the required sections: How to run, API overview, Decisions & trade-offs, Known limitations, Next steps, AI usage & verification.
- **Gate:**
  - `dotnet build` passes;
  - `dotnet tool run dotnet-ef --version` works;
  - `docker compose up -d db` reports healthy.

### P1 — Domain + unit tests
- `Domain/`:
  - `QuoteStatus`, `LineType`, `CustomerInfo`, `QuoteLine` (with `Position`), `Quote`;
  - `Money` (rounding + scale helpers) and `BusinessClock`;
  - `DomainException` and `QuoteErrors` (codes as constants).
- `Quote` exposes intention-revealing methods: `CreateDraft(...)`, `ReplaceDraftContent(...)`, `Send(...)`, `Accept(...)`, `Reject(...)`, `IsExpired(today)`.
  - Every mutation recalculates totals where relevant, sets `UpdatedAt` and increments `Version`.
- Unit tests per PLAN §7 (unit section): each rule R1–R10, with test names referencing the rule. Include the money fixtures and the DST business-date boundaries.
- **Gate:** all unit tests green. In the brief, map each rule → test name(s).

### P2 — Persistence
- `QuotesDbContext` and configurations:
  - snake_case naming;
  - `CustomerInfo` as a **required complex property**;
  - lines as an owned collection in table `quote_lines`, with an internal key and a persisted `position`;
  - precisions per PLAN §3 and string max lengths;
  - `Version` as concurrency token;
  - indexes on `status` and `created_at`;
  - `Number` from a Postgres sequence (nice-to-have; timebox per rule 3).
- Initial migration. Run `dotnet ef migrations script` and **paste the key DDL** in your brief.
- Startup migration behind `Database:MigrateOnStartup`.
- **Gate (round trip, not just "tables exist"):** a throwaway check, or the first integration test if you prefer, against compose Postgres:
  1. create → save → **fresh context** reload;
  2. replace lines (remove one, reorder) and the customer → save → **fresh context** reload;
  3. show that order, values and totals are exactly preserved, including upper-bound numerics.
  - Remove throwaway code afterwards, or keep it as a proper test.

### P3 — Create / Get / List
- Route group `/api/quotes`; `TypedResults`; OpenAPI + Scalar behind `OpenApi:EnableUi`; a small generic FluentValidation endpoint filter; ProblemDetails wiring per conventions.
- `POST` → `201` + `Location` + body. `GET {id}` → body + strong `ETag`. List per PLAN §4.
- `src/CraftQuotes.Api/CraftQuotes.Api.http` with the Kari scenario.
- **Gate:** run the API and exercise it with `curl.exe` (Windows). Show raw responses (status + relevant headers + body) for:
  - create;
  - get;
  - list;
  - one validation error;
  - malformed JSON;
  - an integer enum.

### P4 — Update + transitions + concurrency
- `PUT /api/quotes/{id}`: full replace per PLAN §4 "Create vs replace semantics".
- `/send`, `/accept`, `/reject` with required `If-Match`. `/reject` accepts an empty body.
- **Gate:** show raw `curl.exe` responses for:
  - a successful PUT;
  - PUT without `If-Match` (428);
  - `If-Match: *` (428);
  - a stale tag (412);
  - a weak-only tag (412);
  - `W/"1", "1"` on version 1 (200);
  - `garbage, "1"` (400);
  - PUT after send (409), and PUT after send **with a stale tag** (still 409, per D19);
  - the **stale send** scenario (412, still Draft);
  - accept → 200.

### P5 — Integration tests
- **Harness:**
  1. Start the Testcontainers Postgres **before** creating the `WebApplicationFactory<Program>`.
  2. In `ConfigureWebHost`, override `ConnectionStrings:Quotes` and register a shared `FakeTimeProvider` **before** startup migrations run.
  3. Make `Program` accessible to tests.
- One serialized collection sharing the container. Before each test: truncate tables and reset the shared `FakeTimeProvider` to a fixed instant.
  - Use `AdjustTime`, because `SetUtcNow` can't move backwards. Verify `AdjustTime` exists in the pinned version.
  - Auto-advance stays off; tests use `Advance`.
- Cover every scenario in PLAN §7 (integration section), including the **R11 database concurrency test** with two independent `DbContext`s and a line-only edit.
- **Mutation check:** break one rule (e.g. allow editing a sent quote), show the failing tests, revert, show green.
- **Gate:** full `dotnet test` green; show the summary.

### P6 — Polish and delivery
- Multi-stage `Dockerfile`:
  - `mcr.microsoft.com/dotnet/sdk:10.0` → `mcr.microsoft.com/dotnet/aspnet:10.0` (Ubuntu-based; .NET 10 has no Debian images);
  - non-root user;
  - **verify `Europe/Oslo` resolves inside the container**, and fix it (e.g. install `tzdata`) if it doesn't.
- `api` service in compose, depending on a healthy `db`, with `Database__MigrateOnStartup=true` and `OpenApi__EnableUi=true`.
- Health checks: `/health/live` (no deps), `/health/ready` (DbContext check).
- JSON console logging outside Development.
- **README** (concise, ≤ ~2 pages):
  - prerequisites;
  - `docker compose up --build`;
  - URLs (API, Scalar);
  - how to run tests;
  - the Kari walkthrough, including the `If-Match` flow;
  - API table;
  - decisions & trade-offs (condensed from PLAN §6);
  - known limitations;
  - next steps (PLAN §9).
  - For **"AI usage & verification"**: draft it from AI_LOG, but mark it `<!-- DRAFT: author to rewrite in own words -->`. I'll write the final version myself.
- **Final self-review** (report results):
  - zero warnings;
  - no unexplained TODOs;
  - no unused code or usings;
  - naming consistent;
  - no secrets except clearly dev-only compose credentials;
  - `.gitignore` covers bin/obj/.vs/.idea;
  - a **clean** `docker compose down -v && docker compose up --build` works, and the `.http` scenario passes against the container.

## AI_LOG.md entry template

```markdown
## P<n> — <phase name> (<date>, elapsed <h:mm>)
- **Asked:** <one-line summary of what the AI was asked to do>
- **Produced:** <key files/changes>
- **Decisions made by AI:** <bullets, or "none">
- **Verification:** <commands run + results; manual checks>
- **Corrections / rejected suggestions:** <what was wrong or rejected and why>   ← I fill this in during review
```

## Definition of done

- Every endpoint in PLAN §4 works end-to-end via `docker compose up --build`, with no local .NET SDK required (unless I approved cuts; then this list is updated accordingly).
- Rules R1–R11 are enforced and covered by tests. The integration suite runs against real Postgres, including the R11 DB concurrency test and the stale-send scenario.
- Zero build warnings; all tests green; the CI workflow is written (remote CI status reported separately once I push).
- README covers how to run, decisions/trade-offs, limitations, next steps and AI usage.
- Clean, small commit history: one or more commits per phase.

Start by reading PLAN.md and AI_LOG.md. Before writing any code, confirm your understanding of the plan in 5–10 bullets and list any questions. Then begin P0.
