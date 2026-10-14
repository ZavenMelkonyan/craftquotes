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
