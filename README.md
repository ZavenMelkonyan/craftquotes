# CraftQuotes

A small .NET 10 service for tradespeople to create, edit and send **job quotes**, and record the customer's decision.
Example: an electrician quotes Kari for an EV charger: labour + materials, VAT, a validity date. Kari accepts or rejects.

Beyond plain CRUD it covers server-side money calculation, a small state machine (Draft → Sent → Accepted / Rejected),
expiry by Norwegian business date, and optimistic concurrency so that two people editing the same quote can't
silently overwrite each other. Full design: [docs/ai/PLAN.md](docs/ai/PLAN.md).

## How to run

**Prerequisite:** Docker. No local .NET SDK is needed to run the service.

```bash
docker compose up --build
```

| What | URL |
|---|---|
| API | http://localhost:8080/api/quotes |
| Scalar (OpenAPI UI) | http://localhost:8080/scalar/v1 |
| OpenAPI document | http://localhost:8080/openapi/v1.json |
| Health | http://localhost:8080/health/live, http://localhost:8080/health/ready |

Compose starts Postgres 17 and the API; the API applies EF Core migrations on startup (enabled for compose only).
The credentials in `docker-compose.yml` are local-development values.

**Tests** (need the .NET 10 SDK and a running Docker for Testcontainers):

```bash
dotnet test
```

**Run the API from source** against the compose database: `docker compose up -d db`, then
`dotnet run --project src/CraftQuotes.Api` (http://localhost:5080, Development settings).

## Walkthrough: Kari's quote

[`src/CraftQuotes.Api/CraftQuotes.Api.http`](src/CraftQuotes.Api/CraftQuotes.Api.http) runs this end to end
(VS / Rider / VS Code REST Client; set `@baseUrl` to `http://localhost:8080` for compose).

1. **Create** a draft: `POST /api/quotes` → `201`, `Location`, body with `"version": 1`, totals computed by the server.
2. **Read** it: `GET /api/quotes/{id}` → `ETag: "1"`.
3. **Edit** the draft (Ola adds the forgotten RCD): `PUT` with `If-Match: "1"` → `200`, `"version": 2`.
   Subtotal 18,215.00 · VAT 4,553.75 · Total 22,768.75 NOK.
4. **Lost update prevented:** the office manager also saves with `If-Match: "1"` → `412 Precondition Failed`. Nothing is overwritten.
5. **Send** the version Ola reviewed: `POST /send` with `If-Match: "2"` → `Sent`, now locked.
   Had someone changed the quote after Ola's review, this would also be `412`: you can only send what you saw.
6. **Edit after send** → `409` with `errorCode: "quote.not_editable"`.
7. Kari **accepts**: `POST /accept` with `If-Match: "3"` → `Accepted`. (Or `/reject`, optionally with `{ "reason": "…" }`.)

## API overview

| Method | Route | Purpose | Success | Errors |
|---|---|---|---|---|
| `POST` | `/api/quotes` | Create a draft | `201` + `Location` | `400` |
| `GET` | `/api/quotes/{id}` | Get one quote | `200` + `ETag` | `404` |
| `GET` | `/api/quotes?status=&search=&page=&pageSize=` | List summaries, newest first | `200` | `400` |
| `PUT` | `/api/quotes/{id}` | Replace draft content (incl. all lines) | `200` | `400`, `404`, `409`, `412`, `428` |
| `POST` | `/api/quotes/{id}/send` | Draft → Sent | `200` | `400`, `404`, `409`, `412`, `428` |
| `POST` | `/api/quotes/{id}/accept` | Sent → Accepted (not after expiry) | `200` | `400`, `404`, `409`, `412`, `428` |
| `POST` | `/api/quotes/{id}/reject` | Sent → Rejected; body `{ "reason" }` optional | `200` | `400`, `404`, `409`, `412`, `428` |
| `GET` | `/health/live`, `/health/ready` | Liveness / readiness (database) | `200` | `503` |

- **`If-Match` is required on every mutation of an existing quote.** Use the `ETag` from `GET` or the `version` from
  any response (`If-Match: "3"`). Missing or `*` → `428`; malformed → `400`; stale → `412`.
- Errors are RFC 9457 ProblemDetails; business-rule violations (`409`) carry an `errorCode`
  (`quote.not_editable`, `quote.invalid_transition`, `quote.expired`, `quote.no_lines`, `quote.valid_until_in_past`).
- Precedence: `400` → `404` → `409` → `428` → `412`. Preconditions are checked last (RFC 9110 §13.2.1), so editing a
  sent quote with a stale tag reports the real problem (`409`), not `412`.
- JSON is camelCase; enums are strings only. Amounts are NOK, computed server-side; clients never send totals.
- `POST` applies defaults (`validUntil` = today + 30 days, `lines` = `[]`). `PUT` is a full replace with no defaults:
  `validUntil` and `lines` are required, and omitted optional fields are cleared.

## Decisions & trade-offs

| Decision | Why | Trade-off |
|---|---|---|
| One API project, feature folders | Right-sized; a use case lives in one file | No compile-time layer boundaries; I'd split when a second consumer of the domain appears |
| Minimal APIs + `TypedResults` | Low ceremony, typed results visible to OpenAPI and tests | Controllers are equally valid |
| Rich `Quote` aggregate | Rules sit next to the data, unit-testable without HTTP/DB; validate first, then mutate | More design than an anemic entity |
| Explicit `Version` as concurrency token + strong ETag, `If-Match` on all mutations | One mechanism backs both the HTTP precondition and the DB check (`UPDATE … WHERE version = @loaded`) | Postgres `xmin` would also work; explicit versioning is portable and visible in the domain |
| Explicit `/send`, `/accept`, `/reject` | Transitions are commands with their own rules and future side effects | `PATCH { status }` hides transition rules behind a field update |
| `PUT` replaces the whole draft | Simple, unambiguous, idempotent effect; lines have no external identity | No partial updates (PATCH) |
| Customer stored as a snapshot | A quote is a document; it must show what was sent | No customer registry |
| Totals computed in the domain and persisted | List/sort by total is a column read | Denormalized; the aggregate is the only writer |
| `decimal` + one rounding helper (2 dp, away from zero), per-line rounding | Explicit, testable money policy; excess input decimals are **rejected**, never rounded | Policy, not a claim of accounting-standard compliance; NOK only |
| `TimeProvider`, one clock read per command, `Europe/Oslo` business date | Deterministic tests; expiry at local midnight, including across DST | Zone is configuration, not per tenant |
| `DomainException` → `409` via `IExceptionHandler` | Thin endpoints; violations can't be ignored | `Result<T>` makes failures explicit in signatures |
| Integration tests on real Postgres (Testcontainers) | Numeric coercion, translation and concurrency behave as in production | Slower than in-memory, needs Docker |
| No MediatR / AutoMapper / repositories | `DbContext` is already a unit of work; explicit mapping is grep-able | These pay off in larger codebases |
| Migrations on startup only behind `Database:MigrateOnStartup` | Zero-friction local run | Production would run a migration bundle in the pipeline |

## Known limitations

- No authentication, authorization or multi-tenancy; anyone who can reach the API can act on any quote.
- No delete (quotes are business records) and no revisions after sending.
- NOK only; one VAT total per quote (no breakdown per rate).
- `search` uses `ILIKE '%…%'` without a trigram index; offset paging. Fine at this scale, not at large volumes.
- No idempotency keys: a retried `POST` creates a second quote.
- A lost race between two requests that both pass `If-Match` is proven at database level (two-`DbContext` test) and
  mapped to `412`; there is no HTTP-level test for that exact timing.
- Responses to `POST`/`PUT` echo decimals at in-memory scale (`4750`), `GET` at database scale (`4750.00`); numerically equal.

## Next steps

1. **AuthN/Z and multi-tenancy:** `CompanyId` on every aggregate, tenant from claims, tenant-scoped reads *and* writes,
   per-action permissions; customer acceptance through a separate channel (e.g. a signed link).
2. **Revisions:** revise a sent quote into a new linked revision, keeping history.
3. **Outbox + domain events:** `QuoteSent` → email/PDF; `QuoteAccepted` → create a job.
4. **Idempotency keys** on create for retries from mobile clients in the field.
5. **Catalog and pricing:** products, labour rates, discounts, VAT breakdown per rate, multi-currency.
6. **Search at scale:** `pg_trgm` index, keyset pagination.
7. **Observability:** OpenTelemetry traces/metrics, audit log of state changes.
8. **API hardening:** rate limiting, versioning, `If-None-Match`/`304` on GET.

## AI usage & verification

<!-- DRAFT: author to rewrite in own words -->

AI was used for planning and for writing most of the code; every phase was reviewed by me and verified by running it.
The full log, including what was rejected or corrected, is in [docs/ai/AI_LOG.md](docs/ai/AI_LOG.md).

- **Planning:** Claude drafted the plan; ChatGPT reviewed it independently twice as a skeptical principal engineer.
  Accepted changes included `If-Match` on *all* mutations (closing a "send a version you never reviewed" gap),
  rejecting excess decimals instead of rounding, and evaluating preconditions after business rules (RFC 9110 §13.2.1).
- **Build:** Claude Code implemented the plan phase by phase (scaffold → domain → persistence → endpoints →
  concurrency → integration tests → delivery). Each phase ended with a build with warnings as errors, the test run,
  manual `curl` checks of the HTTP contract, a review brief and a commit.
- **How the output was verified:**
  - Money fixtures (Kari, fractional quantity, VAT midpoint, upper bounds) were derived by hand in the plan before any code existed.
  - Rule-named unit tests (R1–R10) and integration tests against real Postgres, including a two-`DbContext` concurrency test.
  - A **mutation check**: removing the "only drafts are editable" guard made 5 tests fail; restoring it made them pass.
  - Generated migration SQL was reviewed, and the HTTP contract was exercised with `curl` in Development, in Production
    mode and in the container.
- **What the AI got wrong (caught by these checks):** a unique index it added on line positions turned a lost
  concurrent update into a `500` instead of `412`; the two-argument `ILIKE` silently disabled escaping in search; and
  the newest xUnit major version is incompatible with VSTest on the .NET 10 SDK.
