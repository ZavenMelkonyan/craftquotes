# AI Usage Log

How AI tools were used on this assignment, what they produced, how the output was verified, and what was corrected or rejected.
Phase entries are appended during the build. The "Corrections / rejected suggestions" lines are filled in by me during review.

**Tools:** Claude (planning + build via Claude Code), ChatGPT (independent plan review).
**Build start:** 2026-10-14 12:36 (+04:00, local system clock)

---

## Planning — round 1: initial plan (2026-10-14)
- **Asked (Claude):** propose a domain and plan for the assignment that a senior/lead dev can defend; produce PLAN.md, BUILD_PROMPT.md and a review prompt for a second model.
- **Produced:** quotes-for-tradespeople domain (SmartCraft's market), rules R1–R10, API contract, decisions D1–D16, phase plan with review gates.
- **My decisions:** Postgres + Docker; keep the quotes domain; GitHub delivery.

## Planning — round 2: independent review by ChatGPT (2026-10-14)
- **Asked (ChatGPT):** act as a skeptical principal engineer grading the plan; prioritized changes plus likely interview questions.
- **Reviewed by Claude, claims spot-checked** (e.g. .NET 10 dropping Debian container images; RFC 9110 §9.3.4 on validators in PUT responses), then merged.

**Accepted:**
- `If-Match` required on **all** mutations, not just PUT. Real bug: a client could `/send` a version it never reviewed. Added the "stale send" scenario.
- Numeric scale/bounds validation (reject excess decimals instead of letting Postgres round silently), and NOK only.
- Split the concurrency rule: R10 (domain bumps the version) vs R11 (the database rejects stale writes). Added a two-`DbContext` test, including a line-only edit.
- Aggregate encapsulation: private line collection, immutable lines and customer, validate-then-mutate.
- Owned lines need an internal key and a persisted `Position`. Added a fresh-context round-trip gate in P2.
- Docker: `aspnet:10.0` (Ubuntu); Debian images don't exist for .NET 10. Verify the Oslo time zone in the container. Scalar behind a config flag.
- Strong ETag correctness: no time-derived `isExpired` in the detail representation; ETag only on GET (RFC 9110 §9.3.4).
- Precise `If-Match` parsing (`*`, weak, malformed, lists), error precedence, string-only enums, 400 on transitions.
- PUT vs POST semantics (defaults on create only; omitted optional fields cleared on PUT); one clock read per command; DST tests.
- Pinned package versions, a local `dotnet-ef` tool, no `LangVersion=latest`, a precise Testcontainers/WebApplicationFactory harness with per-test reset.
- Toned down overstated rationales: `xmin` *is* viable; UUIDv7 leaks a timestamp and is not a security boundary; PATCH isn't inherently bad. Multi-tenancy next steps now cover write isolation and permissions.

**Rejected or modified:**
- *Cut list, accept/reject, CI and health checks to stretch.* **Rejected (my decision).** Kept the full plan, with an honest 4–4.5 h estimate and a 3h15 checkpoint where I decide on cuts.
- *Drop the mutation-check demonstration.* **Rejected.** It takes ~5 minutes and is direct evidence that AI-written tests catch real defects, which is exactly what the review's own interview question #10 asks about. Kept, together with the added P2 round-trip gate.
- *Run fewer full-suite gates.* **Not adopted.** Before P5 the suite is unit tests only (seconds), so the time saving is negligible.
- *Testcontainers builder now takes the image in the constructor.* **Unverified.** The build agent must check against the installed version rather than trust either model.

**Follow-up correction (Claude, while preparing round 3):** the accepted "reject excess decimals" rule, implemented naively by checking the decimal's scale, would reject valid input like `950.000`, because System.Text.Json preserves trailing zeros. Changed to a significant-decimals check: `value == Math.Round(value, n)`.

## Planning — round 3: second ChatGPT review (2026-10-14)
- **Asked (ChatGPT):** verify the round-2 merge, find regressions, rule on specific HTTP/EF choices, give a ready/not-ready verdict. Settled decisions were declared off-limits.
- **Verdict received:** "not ready". 4 Must and 5 Should/Could items, all narrow. No architecture or scope changes.

**Accepted:**
- `TryParseStrictList` instead of `TryParseList`. The permissive parser skips garbage, which contradicts "malformed → 400". Also reject `*` mixed with tags.
- **Error precedence was wrong:** preconditions are now evaluated *last* (`409` before `428`/`412`), per RFC 9110 §13.2.1. A stale tag on a sent quote → `409`. Recorded as D19, with a combined-failure test.
- Minimal APIs throw `BadHttpRequestException` in Development. The exception handler now maps it to its own status code, verified in both environments.
- `vatRate` default was ambiguous under PUT's "omitted fields are cleared". It's now **required on every line, with no defaults**: tax is never guessed, and Norway has several VAT rates.
- `If-Match` lists: any strong member matching succeeds; weak members never match, but don't invalidate the list.
- `AsReadOnly()` wrapper: returning the private `List` as `IReadOnlyList` could be cast back and mutated.
- Restored the explicit `IsExpired` definition (lost in the round-2 edit).
- `FakeTimeProvider` reset via `AdjustTime` (`SetUtcNow` can't go backwards); to be verified against the pinned version.
- D10 rationale corrected again (SQL `SUM` over persisted line amounts doesn't duplicate rounding).

**Simplification derived from the review (Claude):** with the new handler order (load → domain command → check `If-Match` against the *loaded* version → save), EF's tracked original `Version` already equals the client's version. The "set original value" step from round 2 is redundant and was removed. Less code, same atomic guarantee.

**Not adopted:** none. All items were narrow and verifiable. Claude's recommendation: **stop review rounds here**, because remaining risk is in implementation, not the plan, and the phase gates cover it.

## P0 — Scaffold (2026-10-14, elapsed 0:18)
- **Asked:** scaffold repo: git, solution, central build/package props, `dotnet-ef` local tool, compose (db), CI workflow, README skeleton.
- **Produced:** `global.json`, `Directory.Build.props`, `Directory.Packages.props`, `.editorconfig`, `.gitignore`, `dotnet-tools.json`, `CraftQuotes.slnx` (Api + UnitTests + IntegrationTests), `docker-compose.yml`, `.github/workflows/ci.yml`, `README.md` skeleton.
- **Decisions made by AI:**
  - `xunit.v3` pinned to **3.2.2** (+ `xunit.runner.visualstudio` 3.1.5), not the newest 4.0.2. 4.x depends on `xunit.v3.mtp-v2`, and `dotnet test` on the .NET 10 SDK then fails: "Testing with VSTest target is no longer supported by Microsoft.Testing.Platform". 3.2.2 keeps VSTest mode with only allowed packages. Alternatives (`xunit.v3.mtp-off`, or switching to MTP mode) were outside the agreed constraints.
  - EF Core / ASP.NET Core packages 10.0.13; Npgsql EF 10.0.3 (requires EF ≥ 10.0.4, so the set is coherent).
  - Tool manifest at repo root (`dotnet new tool-manifest` default); single `http` launch profile on port 5080; CI uses `setup-dotnet` with `global-json-file`.
- **Contract clarifications agreed with the author:**
  - `If-Match` is *parsed* before loading (malformed → 400, before 404) and *evaluated* after the domain command (428/412 after 409), per PLAN §4 precedence.
  - A past `validUntil` is allowed on create/PUT (drafts); R4 enforces it only at send. Validation stays clock-independent.
- **Verification:** `dotnet build -warnaserror` → 0 warnings, 0 errors. Throwaway xUnit fact ran under VSTest (1 passed), then removed. `dotnet tool run dotnet-ef --version` → 10.0.13. `docker compose config` valid. CI YAML written, not run remotely.
- **Environment issue (db gate deferred):** Docker Desktop crashes on start because AF_UNIX socket files under `%LOCALAPPDATA%` cannot be removed (Win32 error 1920). First `Docker\run\dockerInference` (stale, from March); after renaming `Docker\run` → `run.stale-20261014` (author-approved), the same failure hit `docker-secrets-engine\engine.sock`, and the socket Docker had just created was also undeletable, so this is a host problem, not a stale file. Stopped at the 15-minute tooling timebox. **`docker compose up -d db` healthy is not yet verified**; author is rebooting, and it will be checked at the start of P2.
- **Corrections / rejected suggestions:** _(author fills in during review)_

## P1 — Domain + unit tests (2026-10-14, elapsed 0:40)
- **Asked:** implement the domain model (Quote aggregate, lines, customer snapshot, money, business clock, errors) and unit tests for R1–R10 per PLAN §7.
- **Produced:** `src/CraftQuotes.Api/Domain/` (`Quote`, `QuoteLine`, `QuoteLineInput`, `CustomerInfo`, `QuoteStatus`, `LineType`, `Money`, `BusinessClock` + `BusinessInstant`, `DomainException`, `QuoteErrors`, `Guard`); `tests/CraftQuotes.UnitTests/` (`QuoteLifecycleTests`, `QuoteLineRulesTests`, `MoneyTests`, `BusinessClockTests`, `TestData`).
- **Decisions made by AI:**
  - `QuoteLineInput` record carries caller data; the aggregate assigns `Position` (1-based request order) and computes amounts via an `internal` `QuoteLine.Create`.
  - Domain guards for R9 and string limits throw `ArgumentException` (an "impossible state" once the validator has run, which surfaces as a 500 if it ever fires); business-state rules throw `DomainException` (→ 409). Limits are `const`s on the domain types so validators and EF configs reuse one source.
  - `SetContent` validates every argument into locals before assigning; `BuildLines` runs before it, so a failed edit leaves the aggregate untouched (R10). `Touch` is the only place that bumps `Version`.
  - `Guid.CreateVersion7(now.Utc)`: the id's timestamp equals `CreatedAt`, from the same clock read.
  - `BusinessClock.Now()` returns a `BusinessInstant(Utc, Today)` from one `GetUtcNow()` call. A test with auto-advance at 23:30 Oslo time would catch a second read.
  - POST defaults (valid-until +30 days, empty lines) stay out of the domain; they belong to the create handler (P3).
  - `Number` deferred to P2: it is assigned by a database sequence, so how it maps decides its CLR shape.
  - CA1707 (underscores in identifiers) disabled **for `tests/**` only** in `.editorconfig`: it conflicts with the rule-named test convention (PLAN §7). Production code keeps the rule.
  - Added `<Using Include="Xunit" />` to the test projects (what the xUnit template does) and `Microsoft.Extensions.TimeProvider.Testing` to the unit tests (allowed list).
- **Verification:** `dotnet build -warnaserror` → 0 warnings, 0 errors. `dotnet test` → 63 passed, 0 failed (unit). P0's deferred gate: after the reboot, `docker compose up -d db` → `postgres:17-alpine` Up (healthy).
- **Corrections / rejected suggestions:** _(author fills in during review)_

## P2 — Persistence (2026-10-14, elapsed 0:50)
- **Asked:** DbContext + EF configurations (snake_case, complex customer, owned lines with internal key + position, precisions, concurrency token, indexes, sequence number), initial migration, startup-migration flag, fresh-context round-trip gate.
- **Produced:** `Infrastructure/Persistence/QuotesDbContext.cs`, `Configurations/QuoteConfiguration.cs`, `Migrations/…_InitialCreate`; `Quote.Number`; `Program.cs` DbContext registration + `Database:MigrateOnStartup`; appsettings (dev connection string = compose dev credentials); `tests/CraftQuotes.IntegrationTests/PostgresFixture.cs` + `PersistenceRoundTripTests.cs`. Also `.gitattributes` (`* text=auto eol=lf`, author-approved, separate `chore:` commit).
- **Decisions made by AI:**
  - `Number` is a `bigint` from the `quote_number_seq` sequence (DB default `nextval`), formatted as `Q-000042` at the API boundary (P3). Formatting inside SQL with `lpad(…, 6)` would silently truncate past 999,999.
  - Enums stored as strings (`varchar(20)`): readable rows, and reordering enum members can't corrupt data.
  - Unique index `(quote_id, position)` on `quote_lines`, plus a unique index on `number`.
  - Lines navigation uses field access (`_lines`), so EF never needs a setter on the read-only `Lines`.
  - The connection string is read lazily inside `AddDbContext` so a test host can override it after `Program` starts (WebApplicationFactory, P5).
  - `QuotesDbContext.Configure(...)` is the single source for `UseNpgsql` + snake_case, shared by the app and the tests.
  - `.editorconfig` marks `Migrations/**` as `generated_code = true`. EF-generated migration code tripped CA1861; it's tool output and gets regenerated.
  - The round-trip gate is kept as a **permanent integration test** on Testcontainers (same `postgres:17-alpine` image), not a throwaway script. The fixture is the start of the P5 harness. The migration was also applied to the compose database via the startup flag.
  - Test collection class named `PostgresCollectionDefinition` (CA1711 rejects the `Collection` suffix).
- **Verification:** `dotnet ef migrations script` reviewed (DDL in the P2 brief). `dotnet run` (Development) against compose → "Applying migration '…_InitialCreate'", `\dt` shows `quotes`, `quote_lines`, `__EFMigrationsHistory`; sequences `quote_number_seq`, `quote_lines_id_seq`. EF logs one `fail:` for the history-table probe on an empty database; that's expected and the migration then applies. `dotnet build -warnaserror` → 0/0. `dotnet test` → unit 63 passed, integration 2 passed, 0 failed.
- **Corrections / rejected suggestions:** _(author fills in during review)_

## P3 — Create / Get / List (2026-10-14, elapsed 0:56)
- **Asked:** route group `/api/quotes` with create, get (strong ETag) and list (filter/search/paging); FluentValidation endpoint filter; ProblemDetails wiring; OpenAPI + Scalar behind a flag; `.http` Kari scenario.
- **Produced:** `Features/Quotes/` (`CreateQuote`, `GetQuote`, `ListQuotes`, `QuoteRequestParts`, `QuoteResponse`, `QuotesOptions`, `QuoteEndpoints`), `Infrastructure/Http/` (`ValidationFilter`, `ApiExceptionHandler`, `QuoteETag`), `Program.cs` wiring, appsettings (`OpenApi:EnableUi`, `Quotes:*`), `CraftQuotes.Api.http`.
- **Decisions made by AI:**
  - Request records use nullable members (`decimal? VatRate`, etc.), so a missing value is a 400 from the validator rather than a silent default.
  - Validation error keys are converted to the JSON path the client sent (`lines[0].vatRate`), not FluentValidation's PascalCase names.
  - The list `status` is bound as a string and checked against enum names. Minimal APIs' enum binding uses `Enum.TryParse`, which would accept `status=1`.
  - `search` uses `ILIKE` on title and customer name, with `%`, `_` and `\` escaped so input matches literally.
  - The list projects in SQL; only the `Q-000001` formatting of the fetched `number` runs client-side (allowed in EF's final projection).
  - The exception handler is named `ApiExceptionHandler`, since it maps both `DomainException` and `BadHttpRequestException`. `QuoteETag` holds the ETag format; `If-Match` parsing joins it in P4.
  - `Quotes:BusinessTimeZone` / `Quotes:DefaultValidityDays` go through `QuotesOptions`; `BusinessClock` and `TimeProvider.System` are singletons.
- **Verification:** `dotnet build -warnaserror` → 0/0; `dotnet test` → 63 unit + 2 integration passed. Manual `curl.exe` against `dotnet run` (Development) + compose Postgres: create → 201 + `Location`, totals 18,215.00 / 4,553.75 / 22,768.75; get → 200 + `ETag: "1"`; list with status + search → 1 item, `isExpired` false; unknown id → 404 ProblemDetails; validation → 400 with `customer`, `lines[0].quantity` (1.0001) and `lines[0].vatRate` (missing), while `unitPrice: 950.000` passed; malformed JSON and integer enum → 400 ProblemDetails; `status=1` → 400. Same binding failures checked with **`ASPNETCORE_ENVIRONMENT=Production`** → 400 ProblemDetails (filled by `UseStatusCodePages`). OpenAPI → 404 with the flag off, 200 for `/openapi/v1.json` and `/scalar/v1` with it on.
- **Observed, not changed:** the create response echoes decimals and timestamps at in-memory precision (`4750`, `…57.2873231`), while GET reads them back from Postgres (`4750.00`, `…57.287323`, microseconds). Values are numerically identical; only GET carries an ETag, so strong-ETag semantics hold.
- **Corrections / rejected suggestions:** _(author fills in during review)_

## P4 — Update + transitions + concurrency (2026-10-14, elapsed 1:03)
- **Asked:** `PUT` full replace; `/send`, `/accept`, `/reject` (empty body allowed); `If-Match` required on all four, with the PLAN §4 parsing rules, error precedence and mechanics; `DbUpdateConcurrencyException` → 412.
- **Produced:** `Infrastructure/Http/IfMatch.cs`; `Features/Quotes/QuoteMutation.cs`, `UpdateQuote.cs`, `QuoteTransitions.cs`; `IQuoteContentRequest` + `QuoteContentValidator` in `QuoteRequestParts.cs` (create validator now reuses it); 412 mapping in `ApiExceptionHandler`; endpoints mapped; `.http` extended with the full Kari `If-Match` flow.
- **Decisions made by AI:**
  - `IfMatch` binds itself through `BindAsync`. Parsing happens during parameter binding, so a malformed header is a 400 before the quote is loaded (400 before 404); `Evaluate(loadedVersion)` runs after the domain command (409 before 428/412). Parsing and matching live in one class, as agreed in P0.
  - A malformed header throws `BadHttpRequestException`, which `ApiExceptionHandler` already maps to 400 ProblemDetails in every environment. An empty header value (zero tags) is treated as malformed.
  - Matching uses `EntityTagHeaderValue.Compare(current, useStrongComparison: true)`, so weak members never match but don't invalidate the list.
  - `QuoteMutation.ExecuteAsync` is the **single** implementation of load → command → precondition → save for all four mutations; each handler is a one-liner passing its domain command.
  - `DbUpdateConcurrencyException` → 412 is mapped centrally in `ApiExceptionHandler` instead of try/catch in each handler.
  - POST/PUT share rules through an `IQuoteContentRequest` interface and `QuoteContentValidator` (FluentValidation `Include`, with contravariant `IValidator<in T>`); PUT adds `validUntil` and `lines` as required. This touched the P3 create validator (needed, not a drive-by refactor).
  - `/reject` validator rejects a blank reason ("omit it instead"), so meaningless reasons aren't stored.
  - Domain error messages reworded (`The quote is Accepted; only a Sent quote can be accepted.`) after `curl` showed "A Accepted quote…". This touched P1's `Quote.cs` (wording only).
- **Verification:** `dotnet build -warnaserror` → 0/0; `dotnet test` → 63 unit + 2 integration passed. `curl.exe` against `dotnet run` + compose Postgres: PUT `"1"` → 200 v2; no `If-Match` → 428; `*` → 428; stale `"1"` on v2 → 412; `W/"2"` on v2 → 412; `W/"1", "1"` on v1 → 200; `garbage, "2"` → 400; `*, "2"` → 400; malformed on an unknown id → 400 (not 404); PUT after send with current / stale / no tag → 409 `quote.not_editable` all three (D19); stale send (GET v1 → other PUT v2 → send `"1"`) → 412 and still `Draft` v2; send → accept → 200 `Accepted` v4; reject with empty body (with and without `Content-Type: application/json`) → 200; blank reason → 400; accept after accept → 409 `quote.invalid_transition`. No `fail`/`crit` log lines for these handled errors.
- **Corrections / rejected suggestions:** _(author fills in during review)_
