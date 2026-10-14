# Review Prompt — Round 2, for ChatGPT

> Paste everything below the line into ChatGPT (the same thread as round 1 is fine), then paste the three documents where indicated.

---

This is **round 2** of your review of my take-home plan. You are still the skeptical principal .NET engineer grading a senior/lead submission for a Nordic SaaS company that builds software for construction and trades businesses.

## What happened since round 1
- Your round-1 review was assessed point by point, not accepted blindly. Most points were merged into PLAN.md (now revision 2) and BUILD_PROMPT.md.
- `AI_LOG.md` records exactly what was **accepted**, what was **rejected or modified**, and why.
- Some of your factual claims were independently verified (e.g. no Debian images for .NET 10; RFC 9110 §9.3.4).

## Settled decisions — do not re-litigate
These are deliberate author decisions. Only challenge one if you find a concrete **correctness** problem, not a preference.
- **Full scope is kept** (list, accept/reject, CI, health checks, numbering). Estimate ~4–4.5 h with a 3h15 checkpoint where I decide on cuts.
- Mutation-check demonstration stays.
- Single API project + feature folders; domain exceptions → 409; explicit `Version` column; `PUT` full replace; command endpoints; NOK only.

## Your task
**Do not write code. Do not rewrite the documents.** This round is about integration quality and readiness, not new ideas.

1. **Merge verification.** For each item AI_LOG marks as accepted: is it reflected **correctly and consistently** in both PLAN.md and BUILD_PROMPT.md? Flag anything missed, half-merged, or merged wrongly.
2. **Regressions and contradictions.** Did the revisions introduce new problems? Check:
   - inconsistencies between PLAN and BUILD_PROMPT, or within PLAN (e.g. §4 vs §7 vs D-table vs definition of done);
   - over-specification that makes a coding agent brittle or wastes time.
3. **Scrutinize these specific choices** (each: "correct", "defensible", or "wrong + why"):
   - `If-Match: *` → `428`; weak tag → `412`; malformed tag → `400`.
   - ETag header only on `GET`; mutation responses return `version` in the body; clients may send `If-Match: "<version>"`. Is that coupling acceptable, and is my reading of RFC 9110 §9.3.4 right? Does it matter for `201` / `POST` responses?
   - Error precedence `400 → 404 → 428 → 412 → 409`, given that the validation filter runs before the entity is loaded.
   - PUT clears omitted optional fields; `lines` required (`[]` allowed).
   - R9 significant-decimals check via `value == Math.Round(value, n)` (System.Text.Json preserves trailing zeros, so scale bits are unreliable).
   - Setting EF's **original** `Version` to the client's version so the `UPDATE … WHERE` enforces `If-Match` atomically.
   - `isExpired` removed from the detail representation, kept in list summaries.
4. **Technical accuracy of new specifics** (.NET 10 / EF Core 10 / Npgsql 10). Only claim something exists or doesn't if you're confident; otherwise write "verify".
   - `Microsoft.Net.Http.Headers.EntityTagHeaderValue.TryParseList`;
   - `JsonStringEnumConverter(allowIntegerValues: false)` via `ConfigureHttpJsonOptions` and its effect on Minimal API binding errors;
   - `UseStatusCodePages` + `AddProblemDetails` producing ProblemDetails for binding failures;
   - required complex property + owned collection with internal key on the same entity;
   - `FakeTimeProvider` + Oslo time zone;
   - tzdata in `mcr.microsoft.com/dotnet/aspnet:10.0`;
   - xUnit v3 + VSTest + `WebApplicationFactory` with Testcontainers started first.
5. **Rejections.** Briefly: was any rejection in AI_LOG a mistake? One line each.
6. **Readiness.** Is the build prompt ready to hand to a coding agent? If not, list only the blocking items.

## Output format
1. **Verdict:** 2–4 sentences, ending with **"Ready to build: yes / no"**.
2. **Issues table** (max ~10 rows, ordered by priority):
   `Priority (Must / Should / Could) | Doc + section/ID | Issue | Concrete fix (one or two sentences)`
3. **Specific choices (task 3):** one line each.
4. **Rejections:** one line each.

## Rules
- No new scope and no new features. If something is missing, it must be a correctness or consistency gap.
- Don't repeat round-1 points that were already merged correctly.
- If there are no "Must" issues, say so plainly. "Nothing blocking" is a valid answer.
- Assume an experienced .NET reader. Skip basics.

---

[PLAN.md — revision 2]
<paste docs/ai/PLAN.md>

[BUILD_PROMPT.md]
<paste docs/ai/BUILD_PROMPT.md>

[AI_LOG.md]
<paste docs/ai/AI_LOG.md>
