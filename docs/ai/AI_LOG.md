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

## P5 — Integration tests (2026-10-14, elapsed 1:14)
- **Asked:** a WebApplicationFactory + Testcontainers harness with a shared `FakeTimeProvider` and per-test reset; cover every PLAN §7 integration scenario, including the R11 two-`DbContext` test; mutation check.
- **Produced:** `ApiFactory` (container started before the host; `ConnectionStrings:Quotes`, `Database:MigrateOnStartup` and `TimeProvider` overridden in `ConfigureWebHost`; `ResetAsync` truncates and `AdjustTime`s to a fixed instant), `ApiTest` base (reset in `InitializeAsync`, HTTP + ProblemDetails helpers), `QuoteLifecycleTests`, `IfMatchTests`, `ValidationTests`, `ListQuotesTests`, `DatabaseConcurrencyTests`; round-trip tests moved onto the shared harness (`PostgresFixture` removed). Before P5: `IfMatchOperationTransformer` (author chose option 1) documents the required `If-Match` header in OpenAPI.
- **Bugs found by the new tests (fixed at the root):**
  - **R11 returned the wrong error.** The two-context test got `DbUpdateException` 23505 on `ix_quote_lines_quote_id_position`, not `DbUpdateConcurrencyException`. Every replace re-inserts positions 1..n, so the losing writer's inserts collided with the winner's rows before EF saw the root `UPDATE … WHERE version` affect 0 rows. Over HTTP that is a **500 instead of 412** for exactly the race R11 is about. Fix: removed the unique `(quote_id, position)` index (an AI addition in P2, not in the plan; the aggregate is the only writer of positions). The initial migration was regenerated, since it was never released, and the compose dev volume reset. Data stayed atomic throughout (rollback, no partial lines); only the error class was wrong.
  - **Literal `%` search matched nothing.** The two-argument `EF.Functions.ILike` disables backslash escaping (confirmed in psql: `ILIKE '%100\%%'` is `t` by default, `f` with `ESCAPE ''`). Fix: pass `\` as the escape character explicitly.
- **Decisions made by AI:**
  - One collection fixture (`ApiFactory`) owns both the container and the host; tests also get an independent `DbContext` from it for the round-trip and R11 tests.
  - The binding-failure test runs under both `Development` and `Production` (`WithWebHostBuilder(UseEnvironment(...))`), so the empty-400 path that `UseStatusCodePages` fills is covered by a test as well.
  - `Program` needed no `public partial class Program`: .NET 10 generates a public `Program` for top-level statements (verified: `WebApplicationFactory<Program>` compiles). `FakeTimeProvider.AdjustTime` verified to exist in 10.11.0.
  - Expiry tests advance the fake clock to Oslo midnight (`+12h` from 10:00Z in October = 22:00Z, CEST) and to one second before it.
- **Mutation check:** removed the `Status != Draft` guard in `Quote.ReplaceDraftContent` → **5 failures**: unit `R2_Cannot_edit_sent_quote`, `R7_Terminal_quote_accepts_no_further_changes` (Accepted, Rejected); integration `R2_Edit_after_send_is_409_not_editable`, `D19_PUT_on_sent_quote_with_stale_tag_is_409_not_412`. Reverted with `git checkout` (file identical to HEAD) → all green.
- **Verification:** `dotnet build -warnaserror` → 0/0; `dotnet test` → **63 unit + 51 integration passed, 0 failed, 0 skipped**.
- **Corrections / rejected suggestions:** _(author fills in during review)_

## P6 — Polish and delivery (2026-10-14, elapsed 1:21)
- **Asked:** multi-stage non-root Dockerfile with the Oslo time zone verified; `api` in compose; health checks; JSON logging outside Development; README; final self-review and a clean compose run.
- **Produced:** `Dockerfile`, `.dockerignore`, `api` service in `docker-compose.yml`, `/health/live` + `/health/ready` and JSON console logging in `Program.cs`, `README.md` (AI section marked `DRAFT` for the author to rewrite).
- **Decisions made by AI:**
  - The image publishes only the API (tests run in CI, not in `docker build`). Restore is a separate layer from the project files. Runs as the base image's built-in non-root user (`USER $APP_UID`, uid 1654).
  - No `tzdata` install needed: `aspnet:10.0` (Ubuntu 24.04) already ships `/usr/share/zoneinfo/Europe/Oslo`, and the running container computed today + 30 = 2026-11-13 through `BusinessClock`.
  - Liveness checks nothing external, so a database outage doesn't get the process restarted; readiness runs the `DbContext` check.
  - Compose connection string sets `GSS Encryption Mode=Disable`. Npgsql otherwise probes Kerberos, and the image has no `libgssapi_krb5`, which logged an `Error:` line on every start. Password auth is used, so nothing is lost; this avoids adding packages to the image.
- **Verification:**
  - `dotnet build -warnaserror` → 0/0; `dotnet test` → 63 unit + 51 integration passed.
  - Container: uid 1654 (`app`), Ubuntu 24.04.5, runtime 10.0.13, Oslo zone file present.
  - Clean `docker compose down -v && docker compose up --build`: `/health/live` 200, `/health/ready` 200, `/openapi/v1.json` 200, `/scalar/v1` 200, logs are JSON, no gssapi noise. The `.http` Kari flow against the container: create 201 (`Q-000001`, validUntil 2026-11-13) → GET `ETag: "1"` → list 1 → validation 400 → PUT `"1"` 200 (18,215 / 4,553.75 / 22,768.75) → stale PUT 412 → send 200 → PUT after send 409 `quote.not_editable` → accept 200 v4.
  - An inline `curl` body containing `²` returned 400 because the Windows command line didn't send UTF-8 (the same body from a UTF-8 file → 201). That was a test-tool artefact, and it also confirmed invalid UTF-8 gets a 400 ProblemDetails outside Development.
- **Final self-review:**
  - Zero warnings.
  - No TODO/FIXME/HACK.
  - Unused usings: IDE0005 raised to warning for a one-off build → none. A planted unused `using` was flagged, so the check works; both files restored.
  - Secrets: only the dev-only compose/Development credentials.
  - `.gitignore` covers `bin/`, `obj/`, `.vs/`, `.idea/` (`git check-ignore`), and no build output is tracked.
- **Corrections / rejected suggestions:** _(author fills in during review)_

## Final review — multi-agent bug hunt + fixes (2026-10-14, elapsed 2:26)
- **Asked:** a final check for remaining bugs, then fix the ones the author chose: 1–6 and 8–11, add 12 to Known limitations, fix 7 with a small mapping.
- **How the review ran:** 7 independent finder agents (domain, HTTP contract, persistence, concurrency, test quality, delivery/docs, robustness) probed the code and the running container. Each finding then went to two verifiers: a refuter, checking it against PLAN, AI_LOG decisions and the code, and a reproducer, working in an isolated git worktree with scratch tests and curl. A completeness critic looked for gaps afterwards. 29 raw findings → 12 distinct real issues; 4 disputed items were judged not bugs or out of scope (AI_LOG placeholders are the author's; `/reject -d ''` → 415 is correct because the client declared a form body).
- **Correction to P5:** the P5 entry said the R11 two-`DbContext` test proves the database version predicate. **It did not.** With `IsConcurrencyToken()` removed, all 51 integration tests stayed green, because the losing writer's DELETE of already-replaced line rows affected 0 rows and EF raised the concurrency error for that reason. A reviewer showed that without the token, a racing accept and reject on the same quote both commit. The production code was correct; the test proved less than claimed.
- **Fixed (tests):**
  - New R11 test: the first writer makes a root-only change (`Send`), the second a line edit. Only `WHERE version = @loaded` can reject it.
  - Search test gets a decoy ("Cable 1000 m") and a `_` case.
  - Round trip gets a line at full `numeric(18,3)` / `numeric(5,4)` scale.
  - Unit test for the description length bound.
  - **Mutation checks:** removing `IsConcurrencyToken()`, removing LIKE escaping, and narrowing the quantity/VAT columns in the migration each fail exactly the intended test; restored with `git checkout`, then green.
- **Fixed (code):**
  - `-0` price/VAT now compares instead of testing the sign bit (was 500; now stored as 0).
  - NUL characters rejected in all text fields and `search` (was 500, Postgres error 22021).
  - `page` capped at `int.MaxValue / MaxPageSize` (was overflow → negative OFFSET → 500).
  - Unknown request charset → 415, mapped by `TargetSite` = `HttpRequestJsonExtensions` (was 500).
  - Line `type` bound as a string and validated as exactly one member name, like `status` (`"Labour, Material"` was silently stored as Material).
  - Exception-mapped errors are written with `TypedResults.Problem(...).ExecuteAsync`, so non-JSON `Accept` still gets a ProblemDetails body. Our 404s are ProblemDetails results too.
  - OpenAPI: optional record members have `= null` defaults (no longer "required"); list query parameters are named in camelCase.
  - `.http` PUTs reuse `{{create.response.body.$.validUntil}}` instead of a hardcoded date that would stop working after 2026-11-13.
- **Documented, not fixed (12):** inputs with more than 28 significant digits are rounded by the JSON parser before the scale check (README Known limitations).
- **Decisions made by AI:**
  - Line `type` binding mirrors the existing `status` approach rather than adding a custom JSON converter.
  - The charset mapping keys on the throwing type rather than the message text.
  - Request records were reordered (required members first) so optional ones can take defaults. JSON binds by name, so the wire format is unchanged.
- **Verification:** `dotnet build -warnaserror` → 0/0. `dotnet test` → **64 unit + 66 integration passed**. The 12 new tests all fail against the previous `src/` (stash check) and pass with the fixes. Rebuilt container, checked with curl: `-0` → 201 with `0.00`; NUL title or search → 400; `page=107374184` → 400 and the largest allowed page → 200; `"Labour, Material"` → 400; `charset=bogus` → 415 ProblemDetails; `Accept: text/html` 409 and 404 → `application/problem+json`; zero error-level log lines.
- **Corrections / rejected suggestions:** _(author fills in during review)_
