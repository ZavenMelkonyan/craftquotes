# CraftQuotes — Implementation Plan

A small .NET 10 backend service for creating and managing **job quotes** for tradespeople (electricians, plumbers, carpenters).
This document is the single source of truth for scope, rules, API contract and decisions.

> Revision 3: incorporates two rounds of external review (ChatGPT). What was accepted or rejected, and why, is recorded in `AI_LOG.md`.

---

## 1. The problem — a real-life example

**Kari** (a homeowner in Bergen) just bought an electric car and needs a home charger.
She contacts **Fjord Elektro AS**, a small electrical company. Their electrician **Ola** visits, inspects the fuse box,
and concludes she needs a new 32A circuit, cable, a Type B RCD, the charger unit, and about 5 hours of work.

Back in the van, Ola opens the app and **creates a quote** (status `Draft`):

| # | Type     | Description                     | Qty | Unit | Unit price | VAT | Net       |
|---|----------|---------------------------------|----:|------|-----------:|----:|----------:|
| 1 | Labour   | Installation, electrician       |   5 | h    |     950.00 | 25% |  4,750.00 |
| 2 | Material | EV charger 11 kW                |   1 | pcs  |   8,990.00 | 25% |  8,990.00 |
| 3 | Material | Cable 5x6 mm²                   |  15 | m    |      89.00 | 25% |  1,335.00 |
| 4 | Material | RCD Type B 40A                  |   1 | pcs  |   2,450.00 | 25% |  2,450.00 |
| 5 | Other    | Call-out / travel               |   1 | pcs  |     690.00 | 25% |    690.00 |

**Subtotal 18,215.00 · VAT 4,553.75 · Total 22,768.75 NOK.** Valid for 30 days.

What happens next, and why each part of the API exists:

1. **Edit while Draft.** Ola notices he forgot the RCD and adds it (`PUT`). Drafts are freely editable.
2. **Concurrent edit.** Meanwhile, Ola's office manager opens the same draft to adjust the labour rate. Both press save.
   Without protection, one change silently overwrites the other (the "lost update" problem).
   Every write carries the version the user was looking at (`If-Match`). The second save gets **412 Precondition Failed**, and the client reloads.
3. **Send — what you reviewed is what you send.** Ola reviewed version 3. The office manager then saved version 4 with a different price.
   If Ola now presses Send, the server must **not** send numbers he never saw. `/send` therefore also requires `If-Match`,
   and Ola gets 412. Once sent, the quote is **locked**, because Kari has seen these numbers. Changes after sending would need a new revision (out of scope).
4. **Customer decision.** Kari **accepts** (`POST /accept`), which in a full product would trigger a job/project and
   later an invoice (out of scope). Alternatively she **rejects** with a reason ("too expensive").
5. **Expiry.** If Kari tries to accept after 45 days, the quote has **expired** (prices may have changed). Accepting it fails,
   and Ola would issue a new quote.

This is SmartCraft's customer domain. It naturally exercises create / retrieve / update, real business rules,
money handling, a state machine and concurrency, all within a small scope.

---

## 2. Scope

**In scope**
- Create, get, list (filter + paging), and update draft quotes with ordered line items.
- Lifecycle transitions: send, accept, reject.
- Server-side totals (net / VAT / gross) with explicit rounding and numeric limits. **NOK only.**
- Optimistic concurrency: a version number exposed as a strong `ETag`; `If-Match` required on every mutation of an existing quote.
- Validation and consistent RFC 9457 `ProblemDetails` errors on all API endpoints.
- PostgreSQL via EF Core with migrations; `docker compose` to run everything.
- Unit tests (domain) and integration tests (real Postgres via Testcontainers), including database-level concurrency.
- OpenAPI + Scalar UI, `.http` file with the Kari scenario, GitHub Actions CI.

**Explicitly out of scope** (documented as next steps in the README)
- Authentication/authorization and multi-tenancy.
- Delete (quotes are business records; we'd archive or cancel, not delete).
- Revisions after send, PDF generation, emailing the customer.
- Multi-currency (other currencies have different minor units and rounding; not worth faking).
- Product/price catalog, discounts.
- Domain events/outbox (e.g. "QuoteAccepted → create Job").

---

## 3. Domain model

### Quote (aggregate root)
| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` (v7) | `Guid.CreateVersion7()`, time-ordered → index-friendly |
| `Number` | `string` | Human-readable, e.g. `Q-000042`, from a Postgres sequence *(nice-to-have)* |
| `Title` | `string` (≤ 200) | e.g. "EV charger installation" |
| `Description` | `string?` (≤ 2000) | |
| `Customer` | `CustomerInfo` | Immutable **snapshot** record: `Name` (req, ≤ 200), `Email` (req, ≤ 320), `Phone?` (≤ 50), `SiteAddress?` (≤ 300) |
| `ValidUntil` | `DateOnly` | Inclusive last valid business date |
| `Status` | `QuoteStatus` | `Draft`, `Sent`, `Accepted`, `Rejected` |
| `Lines` | `IReadOnlyList<QuoteLine>` | Backed by a **private** list, exposed only as a read-only wrapper (`AsReadOnly()`), never the list itself (a cast must not allow mutation); only changed through `Quote` methods |
| `Subtotal`, `VatTotal`, `Total` | `decimal` | Computed by the domain only, persisted for list/sort |
| `RejectionReason` | `string?` (≤ 500) | |
| `CreatedAt`, `UpdatedAt`, `SentAt?`, `DecidedAt?` | `DateTimeOffset` | UTC (offset 0), from `TimeProvider` |
| `Version` | `int` | Starts at 1; incremented on every mutation; EF concurrency token; exposed as ETag |

Currency is **always NOK**. It is returned in responses as `"currency": "NOK"` but not accepted as input.

### QuoteLine (immutable value inside the aggregate)
`Position` (1-based, defines order), `Type` (`Labour` / `Material` / `Other`), `Description` (req, ≤ 200), `Quantity`, `Unit` (req, ≤ 10, e.g. `h`, `pcs`, `m`),
`UnitPrice`, `VatRate` (**required, no default**: tax is never guessed, and Norway has several rates), computed `NetAmount`, `VatAmount`, `TotalAmount`.
No identity in the API. In the database it has an internal key plus a persisted `position`; responses are always ordered by position.

### Lifecycle
```
            send()                 accept()  (only if not expired)
 Draft ─────────────────► Sent ─────────────────► Accepted   (terminal)
   │  ▲                     │
   └──┘ update()            └──────────────────► Rejected   (terminal)
                               reject(reason?)
```
**Expired** is derived, never stored: `IsExpired(today) = Status == Sent && today > ValidUntil`. It is always `false` for Draft, Accepted and Rejected.
No background job is needed for correctness.

### Business rules (each must have at least one test named after it)
- **R1** A new quote is always created as `Draft`, version 1.
- **R2** Only `Draft` quotes can be edited (title, description, customer, validity, lines).
- **R3** A draft may have zero lines, but **sending requires at least one line**.
- **R4** Sending requires `ValidUntil >= today`.
- **R5** Only `Sent` quotes can be accepted or rejected.
- **R6** A `Sent` quote cannot be accepted once expired (`today > ValidUntil`). It can still be rejected.
- **R7** `Accepted` and `Rejected` are terminal; no further changes.
- **R8** Totals are computed server-side only; clients never send totals.
- **R9** Line constraints:
  - quantity > 0 and ≤ 100,000, max **3** decimal places;
  - unit price ≥ 0 and ≤ 10,000,000, max **2** decimal places;
  - VAT rate in [0, 1], max **4** decimal places;
  - description and unit required; max 100 lines.

  Values with too many decimals are **rejected, not rounded**, so the DB can never silently coerce inputs the domain already calculated with.
  Only *significant* decimals count: `950.000` is valid. The check is `value == Math.Round(value, n)`, not the decimal's scale,
  because System.Text.Json preserves trailing zeros.
  These bounds keep 100 lines plus VAT well inside `numeric(18,2)`.
- **R10** *(domain)* Every successful mutation increments `Version` and updates `UpdatedAt`. A failed command leaves the aggregate **unchanged**:
  validate first, then mutate.
- **R11** *(persistence)* A write based on a stale version is rejected **by the database predicate**, not only by the pre-check.
  The root row and its lines are saved atomically, so a losing write leaves no partial lines and no changed totals. Rejected user edits are never retried automatically.

**Business date.** "Today" is the business date in `Europe/Oslo` (configurable), not the UTC date. Otherwise a quote would expire at 01:00 (winter) or 02:00 (summer) local time.
Each command reads the clock **once** (`TimeProvider.GetUtcNow()`) and derives both the UTC timestamp and the Oslo date from that single instant.

### Money rules
- `decimal` everywhere. DB columns: `numeric(18,2)` for amounts, `numeric(18,3)` for quantity, `numeric(5,4)` for VAT rate.
- `NetAmount = Round(Quantity × UnitPrice, 2)`; `VatAmount = Round(NetAmount × VatRate, 2)`; `TotalAmount = Net + Vat`.
- Quote totals are the sum of the rounded line amounts. This is a **documented application policy**, not a claim of accounting-standard compliance.
- Rounding uses `MidpointRounding.AwayFromZero` (commercial rounding), in **one** helper.
- Fixtures:
  - Kari: **18,215.00 / 4,553.75 / 22,768.75**.
  - Fractional quantity: `2.5 h × 949.99 = 2,374.975 → 2,374.98`.
  - VAT midpoint: `0.10 × 0.25 = 0.025 → 0.03`.
  - Upper numeric bounds round-trip unchanged through the DB.

---

## 4. API contract

Base path `/api/quotes`. JSON camelCase. Enums are **strings only** (integer values rejected). All API errors are `ProblemDetails`
(health endpoints excluded). Domain errors include an `errorCode` extension.

| Method | Route | Purpose | Success | Errors |
|---|---|---|---|---|
| `POST` | `/api/quotes` | Create a draft | `201` + `Location` | `400` |
| `GET` | `/api/quotes/{id}` | Get one quote | `200` + `ETag` | `404` |
| `GET` | `/api/quotes?status=&search=&page=&pageSize=` | List summaries, newest first | `200` | `400` |
| `PUT` | `/api/quotes/{id}` | Replace draft content (incl. all lines) | `200` | `400`, `404`, `409`, `412`, `428` |
| `POST` | `/api/quotes/{id}/send` | Draft → Sent | `200` | `400`, `404`, `409`, `412`, `428` |
| `POST` | `/api/quotes/{id}/accept` | Sent → Accepted | `200` | `400`, `404`, `409`, `412`, `428` |
| `POST` | `/api/quotes/{id}/reject` | Sent → Rejected; body `{ "reason": "..." }` optional, empty body allowed | `200` | `400`, `404`, `409`, `412`, `428` |
| `GET` | `/health/live`, `/health/ready` | Liveness / readiness (DB) | `200` | `503` |

### Conditional requests
- **`If-Match` is required on every mutation of an existing quote** (`PUT`, `send`, `accept`, `reject`). One rule, no exceptions.
  The condition always refers to the **quote** (parent) version, including for line changes.
- **ETag** = strong tag `"{version}"`, returned on `GET /api/quotes/{id}`.
  - Mutation responses (`201`, `200`) return the full quote body including `version`, but **no `ETag` header**:
    RFC 9110 §9.3.4 forbids validators on a `PUT` response when the stored representation differs from what was sent (we add totals).
    The same rule is applied to every mutation for consistency.
  - Documented contract: clients may send `If-Match: "<version>"` from the body, or `GET` for a fresh ETag.
- **`If-Match` parsing** uses `EntityTagHeaderValue.TryParseStrictList`. The permissive `TryParseList` silently skips invalid parts. No hand-rolled string stripping.
  - Missing header → `428`.
  - `*` alone → `428`. It only asserts existence, not a revision; requiring a specific version is our stricter policy.
  - Malformed, including garbage mixed with valid tags (`garbage, "1"`), or `*` mixed with tags → `400`.
  - A list **matches if any strong member equals the current ETag**. Comparison is strong, treating tags as opaque strings. Weak members (`W/"1"`) can never match, but they don't invalidate other members: `W/"1", "1"` matches version 1.
  - No strong member matches → `412`.
- **Mechanics** (preconditions are evaluated only for a request that would otherwise succeed, per RFC 9110 §13.2.1):
  1. Load the quote → `404` if missing. Remember the loaded `Version`.
  2. Execute the domain command → `409` if a business rule fails (the aggregate is unchanged).
  3. Evaluate `If-Match` against the loaded version → `428` / `412`. Nothing is saved.
  4. `SaveChanges`. EF's `UPDATE … WHERE version = @loadedVersion` uses the tracked original value. That equals the client's version after step 3, so the check is enforced atomically with no extra code.
     A `DbUpdateConcurrencyException` (another writer committed between load and save) → `412`.
- **Detail representation has no time-dependent fields.** `isExpired` would change at midnight without a version change,
  which breaks strong-ETag semantics. Detail exposes `status` + `validUntil`; `isExpired` appears only in (untagged) list summaries.

### Error precedence
`400` malformed body, headers or query → `404` unknown quote → `409` business rule against the current state → `428` missing or `*` `If-Match` → `412` stale version / lost race.

Conditions come last because RFC 9110 §13.2.1 says preconditions are ignored when the request would fail anyway.
Example: `PUT` on an already-sent quote with a stale tag → `409 quote.not_editable`, not `412`. The stale-send scenario is still `412`, because sending the current draft would otherwise succeed.

`409` error codes:
- `quote.not_editable`
- `quote.invalid_transition`
- `quote.expired`
- `quote.no_lines`
- `quote.valid_until_in_past`

### Create vs replace semantics
- **POST (create):** defaults apply. `validUntil` defaults to today + 30 days (`Quotes:DefaultValidityDays`); `lines` default to `[]`.
- **Every line, in POST and PUT:** `type`, `description`, `quantity`, `unit`, `unitPrice` and **`vatRate`** are required. There are no line-level defaults.
- **PUT (full replace):**
  - required: `title`, `customer` (with name + email), `validUntil`, and `lines` (an array; `[]` explicitly clears; a missing or `null` `lines` → `400`);
  - omitted optional fields are **cleared**;
  - no date defaults are applied, so the result never depends on when the request ran;
  - line order in the request becomes `Position` order.

### List
Query parameters:
- `status` (optional);
- `search` (case-insensitive match on customer name or title);
- `page` ≥ 1 (default 1);
- `pageSize` 1–100 (default 20).

Response: `{ items, page, pageSize, totalCount }`. Ordered `createdAt desc, id desc`. Items are summaries: id, number, title, customerName, status, isExpired, total, currency, validUntil, createdAt.

Example create request:
```json
POST /api/quotes
{
  "title": "EV charger installation",
  "customer": { "name": "Kari Nordmann", "email": "kari@example.com", "siteAddress": "Fjellveien 12, 5019 Bergen" },
  "validUntil": "2026-11-13",
  "lines": [
    { "type": "Labour",   "description": "Installation, electrician", "quantity": 5,  "unit": "h",   "unitPrice": 950,  "vatRate": 0.25 },
    { "type": "Material", "description": "EV charger 11 kW",          "quantity": 1,  "unit": "pcs", "unitPrice": 8990, "vatRate": 0.25 },
    { "type": "Material", "description": "Cable 5x6 mm²",             "quantity": 15, "unit": "m",   "unitPrice": 89,   "vatRate": 0.25 },
    { "type": "Material", "description": "RCD Type B 40A",            "quantity": 1,  "unit": "pcs", "unitPrice": 2450, "vatRate": 0.25 },
    { "type": "Other",    "description": "Call-out / travel",         "quantity": 1,  "unit": "pcs", "unitPrice": 690,  "vatRate": 0.25 }
  ]
}
```

---

## 5. Architecture and tech

- **.NET 10 (LTS)**, ASP.NET Core **Minimal APIs** with `TypedResults` and route groups.
- **One API project, feature folders (vertical slices)**: `Domain/`, `Features/Quotes/`, `Infrastructure/`.
- **EF Core 10 + Npgsql 10**, snake_case naming, code-first migrations, `dotnet-ef` as a local tool (tool manifest).
- **FluentValidation** for request validation (400). The domain enforces invariants independently (409, or an argument guard for impossible states).
- **Built-in** `ProblemDetails` + `IExceptionHandler`, `TimeProvider`, health checks, OpenAPI (+ Scalar UI behind a config flag), JSON console logging.
- **Tests:** xUnit v3 (VSTest runner), Shouldly, `WebApplicationFactory`, Testcontainers.PostgreSql, `FakeTimeProvider`.
- **Delivery:** `docker compose up --build` runs Postgres + API (`mcr.microsoft.com/dotnet/aspnet:10.0`, Ubuntu-based; .NET 10 ships no Debian images). GitHub Actions runs build + all tests.

```
src/CraftQuotes.Api/
  Domain/                 Quote, QuoteLine, CustomerInfo, QuoteStatus, LineType, Money, BusinessClock, DomainException
  Features/Quotes/        CreateQuote, GetQuote, ListQuotes, UpdateQuote, QuoteTransitions, QuoteResponse, QuoteEndpoints
  Infrastructure/
    Persistence/          QuotesDbContext, Configurations/, Migrations/
    Http/                 IfMatch parsing, DomainExceptionHandler, ValidationFilter
  Program.cs
  CraftQuotes.Api.http
tests/CraftQuotes.UnitTests/
tests/CraftQuotes.IntegrationTests/
```

---

## 6. Key decisions and trade-offs

| ID | Decision | Why | Trade-off / alternative |
|---|---|---|---|
| D1 | Quotes for tradespeople | Matches SmartCraft's market; gives real rules beyond CRUD at modest cost | Plain CRUD on a thin entity is faster but shows less |
| D2 | Single API project + feature folders | Right-sized; everything for a use case is in one place | Multi-project Clean Architecture enforces boundaries via references. I'd split when a second consumer of the domain appears or the team grows |
| D3 | Minimal APIs + `TypedResults` | Low ceremony; return types visible to OpenAPI and tests | Controllers are equally valid, with more boilerplate |
| D4 | Rich aggregate with encapsulated invariants | Rules live next to the data they protect; unit-testable without HTTP/DB | A service layer can centralize rules too; the cost is that entities can then be mutated around it |
| D5 | Customer stored as an immutable **snapshot** | A quote is a document; it must show what was sent even if the CRM record changes | Separate Customer resource: more CRUD surface, not the point here |
| D6 | `PUT` replaces the whole draft, including lines | Simple, unambiguous, idempotent; lines have no external identity | PATCH (merge/JSON Patch) is fine for partial updates but needs precise null/absent semantics. Not needed for drafts edited as a whole |
| D7 | Explicit transition endpoints | Transitions are commands with their own rules and future side effects (email, job creation); intent is explicit | `PATCH { status }` works, but puts transition validation behind a generic field update |
| D8 | Explicit `Version` column as concurrency token; strong ETag; `If-Match` required on all mutations | Portable, visible in the domain, unit-testable, directly backs the ETag | Postgres `xmin` is viable here, since every mutation touches the root row via `UpdatedAt`/`Version`. I chose explicit versioning for portability and clarity; it requires every writer to bump it (enforced in one place) |
| D9 | Domain throws `DomainException(code)` → `IExceptionHandler` → `409 ProblemDetails` | Endpoints stay thin; invariant violations can't be silently ignored | `Result<T>` makes failures explicit in signatures; reasonable in a larger codebase |
| D10 | Totals computed in the domain, **persisted** on the quote | List/sort/filter by total is a plain column read; the aggregate is the only writer, so the denormalized totals stay consistent | SQL `SUM` over persisted line amounts also works: no denormalization to maintain, but aggregation work on every list query |
| D11 | `Guid.CreateVersion7()` ids + sequence-based `Number` | Index-friendly; doesn't reveal business volume like sequential ints | UUIDv7 **embeds a creation timestamp** and is not a security boundary. Authorization must never rely on id secrecy |
| D12 | `TimeProvider`, single instant per command, Oslo business date | Deterministic tests; correct local expiry, including across DST | `DateTime.Now`: untestable and wrong around midnight |
| D13 | Integration tests on real Postgres (Testcontainers) | EF InMemory/SQLite differ in translation, constraints, numeric coercion and concurrency | InMemory is faster but gives false confidence |
| D14 | No MediatR / AutoMapper / repository-over-EF | Fewer moving parts; `DbContext` is already a unit of work; explicit mapping is grep-able | These help in larger codebases with cross-cutting pipelines; not needed here |
| D15 | Auto-migrate on startup only behind a config flag (dev/compose) | Zero-friction local run | Production: migration bundle / pipeline step, never on app start |
| D16 | Shouldly for assertions | FluentAssertions v8+ is commercially licensed | FluentAssertions v7 / AwesomeAssertions / plain `Assert` |
| D17 | ETag only on `GET`; mutation responses return `version` in the body | Follows RFC 9110 §9.3.4 (no validators on transformed `PUT` responses). Omitting it on `POST`/`201` too is a **consistency policy**, not an RFC requirement | Many APIs send ETags on writes anyway; costs one extra GET or using `version` |
| D19 | Preconditions evaluated last (`409` before `428`/`412`) | RFC 9110 §13.2.1: conditions only matter for a request that would otherwise succeed; the caller gets the more useful error | Checking `If-Match` first is common and simpler to reason about, but reports "stale" when the real problem is "not editable" |
| D18 | NOK only | Correct rounding for other currencies needs per-currency minor units; faking it adds risk | Multi-currency is a next step |

---

## 7. Testing strategy

**Unit tests (domain, fast, no I/O)**
- Every rule R1–R10, with test names referencing the rule (e.g. `R2_Cannot_edit_sent_quote`).
- R10: a failed command leaves state and `Version` unchanged.
- Money theory cases: Kari fixture, fractional quantity, VAT midpoint, zero price, upper bounds.
- Business date: last valid day vs. the next local midnight, in **winter (CET)** and **summer (CEST)**.

**Integration tests (HTTP → real Postgres)**
- Full lifecycle: create → get (ETag) → update → send → accept, with `If-Match` throughout.
- Validation → `400` ProblemDetails for:
  - missing customer;
  - a line without `vatRate`;
  - malformed JSON;
  - integer enum;
  - too many decimals (while `950.000` passes);
  - rejection reason too long.
  - Binding failures must produce ProblemDetails in **Development** too (where Minimal APIs throw `BadHttpRequestException`).
- Unknown id → `404`. Missing `If-Match` and `*` → `428`. Stale or weak-only tag → `412`. `W/"1", "1"` matches version 1. Malformed (`garbage, "1"`) or `*` mixed with tags → `400`.
- **Precedence:** `PUT` on a sent quote with a stale tag → `409 quote.not_editable`, not `412`.
- **Stale send:** GET v1 → another client PUTs (v2) → send with `If-Match: "1"` → `412`, and the quote is still `Draft`.
- Edit after send → `409 quote.not_editable`; accept after expiry (advance `FakeTimeProvider`) → `409 quote.expired`.
- **Round trip (fresh `DbContext`):** create → reload → replace lines (remove + reorder) and customer → reload. Order, values and totals are exactly preserved, including upper numeric bounds.
- **R11 database concurrency:** load the same quote in two independent `DbContext`s and modify both, including a **line-only edit with unchanged totals**.
  The first save succeeds; the second throws `DbUpdateConcurrencyException`. Reload: only the first edit is present, with no partial lines.
- List: status filter, search, paging metadata.

**Isolation:** one Postgres container shared by a single serialized test collection. Before each test: truncate tables and reset the shared `FakeTimeProvider` to a fixed instant.
`SetUtcNow` can't move backwards, so use `AdjustTime` (verify it exists in the pinned version), with auto-advance off. Tests use `Advance` to move time forward. Each test creates its own data.

**Mutation check (5 min, P5):** temporarily break one rule (e.g. allow editing a sent quote), confirm the right tests fail, revert, and confirm green again.
This is evidence the AI-written tests actually verify behavior, rather than just agreeing with the implementation.

---

## 8. Phases and time budget

The full plan is kept (author's decision). The estimate is honest: **~4–4.5 h including review**.

| Phase | Content | Time |
|---|---|---|
| P0 | Scaffold: git, solution, props, central packages, tool manifest, compose (db), CI, README skeleton | 20 min |
| P1 | Domain model + money + business clock + unit tests | 45 min |
| P2 | Persistence: DbContext, configurations, migration, migrate flag, round-trip check | 35 min |
| P3 | Create / Get / List endpoints, validation, ProblemDetails, OpenAPI/Scalar, `.http` | 40 min |
| P4 | Update + transitions, `If-Match` handling, concurrency mapping | 45 min |
| P5 | Integration tests + mutation check | 45 min |
| P6 | API Dockerfile in compose, health checks, README, clean-start verification | 30 min |

**Checkpoint at 3h15 elapsed:** the agent reports remaining work and I decide whether to apply cuts. The agent never cuts scope on its own.

**Cut list (only applied on my instruction, in this order):**
1. Sequence-based `Number`
2. `search` on list
3. API container in compose (keep db only, run the API with `dotnet run`)
4. Health checks

Applying a cut updates README, API table and definition of done in the same commit.

**Never cut:**
- domain rules + unit tests;
- `If-Match` concurrency;
- the R11 DB test;
- integration tests for the main flows;
- README.

---

## 9. What I'd do next (README "next steps")
1. **AuthN/Z + multi-tenancy:**
   - `CompanyId` on every aggregate;
   - tenant resolved from authenticated claims;
   - tenant-scoped reads **and writes** (filters alone don't protect writes or raw SQL);
   - permission checks per action (who may send, edit, accept);
   - customer acceptance through a separate authenticated channel (e.g. signed link), not the company API.
2. **Revisions:** "revise" a sent quote → a new revision linked to the original; history preserved.
3. **Outbox + domain events:** `QuoteSent` → email/PDF to the customer; `QuoteAccepted` → create Job/Project.
4. **Idempotency keys** on `POST` create (safe retries from mobile clients in the field).
5. **Catalog & pricing:** products, labour rates, discounts, VAT breakdown per rate on the document; multi-currency.
6. **Search at scale:** `pg_trgm` index for `search`; keyset pagination.
7. **Observability:** OpenTelemetry traces/metrics; audit log of state changes.
8. **API hardening:** rate limiting, API versioning, `If-None-Match` / `304` on GET.

---

## 10. Interview prep — likely questions

1. *"The assignment asked for CRUD. Why a quote lifecycle?"* Minimum domain behavior worth showing, its cost, what I cut.
2. *"Show me exactly where a lost update becomes impossible."* Client's observed version → `If-Match` check against the loaded version → EF's `WHERE version = @loaded` predicate → atomic root + lines → 412 → the R11 test.
3. *"I review v3, someone changes the price, then I press Send?"* Stale **intent** vs overlapping requests; why the state machine alone can't protect the reviewed price.
4. *"Why not `xmin` when you already update the root row?"* It's viable. Explicit versioning is a deliberate aggregate concept, portable and unit-testable, at the cost of discipline (bumped in one place).
5. *"Can your ETag stay the same while GET returns different content?"* No: no time-derived fields in detail; deterministic line order; version vs representation validator.
6. *"What does retrying this PUT do?"* Full replacement, cleared optional fields, idempotent effect. The retry gets 412 because the version moved; idempotency ≠ identical status codes.
7. *"Can valid JSON produce persisted inputs that disagree with totals?"* Scale rejection, bounds, calculation order, DB coercion, fresh-context round-trip test.
8. *"When exactly does a quote expire? DST?"* Inclusive Oslo business date from one captured instant; UTC timestamps; boundary tests in winter and summer.
9. *"How do you stop one company accepting another's quote?"* AuthN, tenant-scoped reads and writes, per-action permissions, customer acceptance channel.
10. *"Stale tag on an already-sent quote — 409 or 412?"* 409: RFC 9110 §13.2.1, preconditions only apply to requests that would otherwise succeed (D19).
11. *"Why not Clean Architecture / MediatR / Result<T>?"* D2, D9, D14: right-sizing, and what would change my mind.
12. *"What did the AI get wrong, and how do you know its tests aren't just agreeing with its implementation?"* AI_LOG: concrete rejected suggestions, independently derived fixtures, reviewed SQL, R11 DB test, mutation check.
