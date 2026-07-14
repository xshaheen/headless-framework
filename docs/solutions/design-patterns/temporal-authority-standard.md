---
title: Temporal authority — the framework-wide date/time standard
date: 2026-07-13
category: design-patterns
module: Cross-cutting
problem_type: design_pattern
component: framework
severity: high
applies_when:
  - Any code reads a current time, persists an instant, or compares two instants
  - A timestamp is written by one process and evaluated by another
  - A schedule is expressed in human wall-clock terms
  - A duration or deadline is measured
related_components:
  - background_job
  - messaging
  - caching
  - distributed_lock
  - service_class
tags:
  - temporal-authority
  - clock-skew
  - timeprovider
  - utc
  - dst
  - monotonic-time
  - serialization
---

# Temporal authority — the framework-wide date/time standard

This is the umbrella document for every time-related decision in the framework. It exists because the
framework independently arrived at three *separately correct but mutually inconsistent* time policies
(Messaging on the app clock, Jobs split across both, Coordination on the store clock). Each was defensible
in isolation; together they were an unstated inconsistency that produced real defects.

The specific relational-lease mechanics live in
[atomic-database-clock-relational-lease-claims.md](atomic-database-clock-relational-lease-claims.md).
**This document is the general rule that mechanic is an instance of.**

## The core rule

> **Time semantics belong to whichever authority owns the decision — never to the ambient environment of
> whichever process happens to be executing.**

"Ambient environment" means the host's wall clock, the host's local timezone, and the host's
`DateTime.Kind` defaults. Correctness must never depend on any of them.

Concretely, every timestamp in the framework answers exactly one of four questions, and each question has
exactly one correct authority:

| The timestamp answers… | Authority | Mechanism |
|---|---|---|
| **"Who owns this, and until when?"** (leases, locks, liveness, visibility) | **The store** | DB/Redis server clock, inlined into the atomic statement |
| **"How long has this taken?"** (timeouts, backoff, deadlines, rate limits) | **A monotonic counter** | `TimeProvider.GetTimestamp()` / `GetElapsedTime()` |
| **"When should this fire in human terms?"** (cron, business calendars) | **The tz database** | `TimeZoneInfo` + explicit `TimeZoneInfo`, never `TimeZoneInfo.Local` |
| **"When did this happen?"** (audit, `CreatedAt`, logs, metrics) | **The app clock** | Injected `TimeProvider` |

The failure mode in every historical bug found in this repo was **using the wrong row of that table**.

## 1. Ownership time — the store is the authority

Applies to: `LockedUntil`, lease deadlines, `LastBeat` liveness, message visibility, semaphore slots.

**Rule:** a timestamp that one node writes and a *different* node later compares against "now" must be
**both written by and compared against the store's clock**, inside a single atomic statement.

**Why:** a timestamp is meaningful only relative to the clock that evaluates it. If node A stamps
`LockedUntil = A_now + 30s` and node B evaluates `LockedUntil <= B_now`, then the effective lease duration
is `30s − skew(A, B)`. A fast observer reclaims live work early (duplicate execution); a slow observer
delays recovery.

**Never** sample the clock into a variable and bind it as a parameter — that reintroduces the app clock
*and* adds a round-trip whose latency silently shortens the lease.

```sql
-- WRONG: application clock on both sides, plus a TOCTOU gap
WHERE locked_until <= @applicationNow
SET   locked_until  = @applicationNowPlusLease

-- RIGHT: one statement, one server-time snapshot, used for both eligibility and the stamp
WITH claim_clock AS MATERIALIZED (SELECT clock_timestamp() AS now), ...
SET locked_until = claim_clock.now + (@leaseSeconds * INTERVAL '1 second')
```

**Pass a duration, never an absolute deadline**, across any API boundary that reaches the store. The store
derives the deadline from its own clock.

Redis follows the identical rule via a different mechanism: send **relative** durations (`PX`, `PEXPIRE`)
or compute inside the Lua script with `redis.call('TIME')`. **Never** `PEXPIREAT` with a client-computed
absolute epoch — that is the app clock wearing a server-side disguise.

Mechanics, per-provider SQL shapes, and the EF-translation caveats: see
[atomic-database-clock-relational-lease-claims.md](atomic-database-clock-relational-lease-claims.md).

### Provider function reference

| | PostgreSQL | SQL Server |
|---|---|---|
| **Use** | `clock_timestamp()` (real time) | `SYSUTCDATETIME()` (`datetime2`, 100 ns) |
| **Never** | `now()`, `CURRENT_TIMESTAMP` — **transaction-start time**, frozen for the transaction's life | `GETUTCDATE()` — returns `datetime`, **~3.33 ms** precision, rounded to 1/300 s |
| Column | `timestamptz` | `datetime2(7)` |
| Atomic claim | `FOR UPDATE SKIP LOCKED` + `UPDATE … RETURNING` | `UPDLOCK, READPAST, ROWLOCK` + `OUTPUT inserted.*` |

Within a single statement, PostgreSQL's `statement_timestamp()` is preferable to `clock_timestamp()`:
it is **stable across the statement**, so the `WHERE` arm and the `SET` arm cannot land on different
instants. `clock_timestamp()` advances *during* execution.

> **EF-translation trap.** Npgsql translates `DateTime.UtcNow` to `now()` — i.e. **transaction-start
> time**, not real time. SQL Server's EF provider translates it to `GETUTCDATE()` (3.33 ms). An
> `ExecuteUpdate` expression tree therefore does **not** give you the same semantics as hand-written
> native SQL. This is self-consistent inside a single autocommit statement, and silently wrong the moment
> the call is wrapped in a transaction. Prove provider translation with an integration test; never infer
> it from LINQ.

## 2. Elapsed time — a monotonic counter is the authority

Applies to: timeouts, acquire-wait loops, backoff, grace periods, rate limits, circuit breakers.

**Rule:** use `TimeProvider.GetTimestamp()` / `GetElapsedTime()`. Never subtract two wall-clock readings.

```csharp
var start = _timeProvider.GetTimestamp();
...
var elapsed = _timeProvider.GetElapsedTime(start);
```

**Why:** wall-clock arithmetic breaks under NTP steps and VM live-migration. A backward step inflates a
computed remaining-wait; a forward step fires a timeout early.

`Stopwatch` and `Environment.TickCount64` are monotonic and therefore *safe*, but they are **not fakeable** —
they cannot be driven by `FakeTimeProvider` in tests. `TimeProvider.GetTimestamp()` is monotonic **and**
fakeable, so it is strictly better. Prefer it over both.

Likewise, for delays and timers use `Task.Delay(…, timeProvider)`,
`timeProvider.CreateCancellationTokenSource(…)`, and `TimeProvider.CreateTimer(…)` — never a raw
`new Timer(…)`.

**Backoff must be** monotonic, capped, jittered, and clamped non-negative — *including* caller-supplied
intervals. Any delay that crosses a public API boundary is untrusted input: clamp it.

## 3. Wall-clock scheduling — the tz database is the authority

Applies to: cron expressions, business calendars, "every day at 09:00 local".

This is the one case where a *local* time is the correct domain — a human said "09:00", and they meant
09:00 in a named zone, not an instant. The instant must be **derived** from the wall-clock intent, and the
derivation has two edge cases that a naive `ConvertTimeToUtc` gets wrong.

`src/Headless.Jobs.Core/CronScheduleCache.cs` is the reference implementation. Both DST transitions are
handled deliberately:

**Spring-forward gap** (a local time that does not exist). `02:30` in a one-hour gap is invalid.
Naive conversion collapses *every* skipped occurrence onto the boundary (`03:00`), stacking them. The
correct behaviour is to **shift the requested wall-clock minute through the gap**, preserving its offset
from the boundary:

```csharp
if (TimeZoneInfo.IsInvalidTime(localTime))
{
    // 02:30 in a one-hour gap becomes 03:30 — not collapsed to 03:00.
    var offsetBefore = TimeZoneInfo.GetUtcOffset(localTime.AddDays(-1));
    var offsetAfter  = TimeZoneInfo.GetUtcOffset(localTime.AddDays(1));
    var gap = offsetAfter - offsetBefore;
    localTime = localTime.Add(gap > TimeSpan.Zero ? gap : TimeSpan.FromHours(1));
}
```

**Fall-back overlap** (a local time that happens twice). `01:30` occurs once in DST and again in standard
time. Naive conversion picks one arbitrarily — or worse, the job fires **twice**. The correct behaviour is
to pick a single deterministic instant (the later one, i.e. the standard-time offset) so one wall-clock
occurrence runs exactly once:

```csharp
if (TimeZoneInfo.IsAmbiguousTime(localTime))
{
    // Choose the later UTC instant so one wall-clock occurrence runs once, not twice.
    var offset = TimeZoneInfo.GetAmbiguousTimeOffsets(localTime).Min();
    return new DateTimeOffset(localTime, offset).UtcDateTime;
}
```

There is a third, subtler case that `GetNextOccurrenceOrDefault` also handles: when the *search origin*
is itself inside an ambiguous hour, an occurrence can be hiding in the repeated hour **earlier** than the
one a forward scan finds. The implementation re-scans from `localTime − overlap` and takes the earlier
valid instant if it is still in the future.

**Never default a scheduler's timezone to `TimeZoneInfo.Local`.** In a fleet, nodes may resolve different
local zones, compute different UTC instants for the same cron expression, and — because occurrences dedup
on `(JobId, ExecutionTime)` — create *separate, non-deduped* rows. That is duplicate execution of one
logical tick. Require an explicit `TimeZoneInfo`; default to `TimeZoneInfo.Utc`.

**Never reinterpret a `Kind=Unspecified` instant through a scheduler timezone.** If a contract says "UTC",
enforce it at the type level (`DateTimeOffset`) rather than silently applying a local offset.

## 4. Observational time — the app clock is the authority

Applies to: `CreatedAt`, `UpdatedAt`, `DeletedAt`, audit entries, log timestamps, metric timestamps.

**Rule:** injected `TimeProvider` (or the `TimeProvider`-backed clock). Never `DateTime.UtcNow` directly —
it is untestable. Never `DateTime.Now` — ever.

These timestamps record *what happened*, not *who owns what*. Cross-node skew makes them slightly
imprecise; it does not make them incorrect. They do not need the store clock — **unless they also
participate in an ownership or expiry predicate**, in which case they are ownership time (§1) and the
store owns them. Jobs deliberately stamps `UpdatedAt` from the claim's DB snapshot precisely because it
doubles as an optimistic-concurrency fence.

**Logs must be UTC.** Serilog captures `LogEvent.Timestamp` from `DateTimeOffset.Now` (local) by default —
override it.

## 5. Storage and representation

- **Persisted instants and public API contracts use `DateTimeOffset`.** `DateTime` carries a `Kind` that
  is trivially lost — by EF materialization, by serializers, by provider SDKs — and a doc-comment is not
  a type-system guarantee. `DateTimeOffset` makes "this is an instant" unforgeable.
- **Columns:** PostgreSQL `timestamptz`; SQL Server `datetime2(7)`. Never SQL Server `datetime` (3.33 ms).
  Never PostgreSQL `timestamp` (no zone) for an instant — it cannot express "this is UTC" and relies
  entirely on convention.
- **Never trust `DateTime.Kind` from an external SDK.** AWS S3 returns `LastModified` with
  `Kind=Unspecified`; `new DateTimeOffset(DateTime)` then applies the **host's local offset**, silently
  shifting the value on any non-UTC host. Always normalize explicitly:

  ```csharp
  new DateTimeOffset(DateTime.SpecifyKind(raw, DateTimeKind.Utc), TimeSpan.Zero)
  ```

- **Serialization:** ISO 8601 round-trip (`"O"`). Never a culture-general `ToString()` — the invariant
  "G" pattern silently truncates to whole seconds. Any serializer that cannot preserve `Kind`/offset
  (MessagePack's native Timestamp format collapses `Kind`) must document the limitation and be covered by
  a round-trip test that asserts on it.

> **Testing trap.** `DateTime.Equals` / `==` compares **only Ticks and ignores `Kind`**. A round-trip test
> written as `result.Should().Be(original)` passes even when the serializer destroyed the `Kind`. Assert
> on `.Kind` (or use `DateTimeOffset`) or the test proves nothing.

## 6. Testing rules

- Default to a **frozen clock** (`FakeTimeProvider`). A test that sleeps to observe time-based behaviour
  is a flaky test.
- Wall-clock waits are acceptable **only** where a real server's clock genuinely cannot be faked (Redis
  TTL, PostgreSQL/SQL Server lease expiry). Label them as such.
- **Skew tests are mandatory** for anything in §1. Inject a deliberately skewed `TimeProvider` and assert
  the outcome follows the *store* clock, not the app clock. Reference:
  `JobsCoordinationConformanceTests.stalled_reclaim_uses_the_db_clock_not_a_skewed_reclaimer_clock`.
- **Exactly-once claim under concurrency** must be tested with N racing workers asserting
  `OnlyHaveUniqueItems()`, not sequentially.
- Test generic-EF and native-provider strategies **separately**. A shared interface does not prove their
  SQL preserves the same clock invariant.
- Assert deadlines with a **bounded interval**, never exact timestamp equality (providers truncate).
- **Run the suite under a non-UTC timezone** (`TZ=Africa/Cairo`) in CI. A UTC-only dev box and a UTC-only
  CI box cannot catch a local-offset bug — which is exactly how the AWS `LastModified` defect survived.

## Why this matters

Every time defect found in the July 2026 audit reduces to one of these — the same small mistake, made
independently in nine places:

| Defect (all now fixed) | Wrong authority used |
|---|---|
| Messaging lease steal under skew | Ownership time taken from the app clock |
| Jobs claim/reclaim skew window | Ownership time split across two clocks |
| `now()` / `GETUTCDATE()` for lease math | Ownership time at transaction-start / 3.33 ms precision |
| S3 `LastModified` off by the host's offset | Trusted an external SDK's `Kind` |
| Redis `PEXPIREAT` sorted-set expiry | Ownership time smuggled in as an absolute app epoch |
| Serilog local timestamps | Observational time from the ambient local zone |
| NATS lockstep reconnect | Backoff with no jitter |
| Jobs unclamped `RetryIntervals` | Untrusted caller input used as a delay |
| Cron `TimeZoneInfo.Local` default | Wall-clock scheduling from the ambient local zone |

None were caused by carelessness — each was locally reasonable, and several were *deliberate*, with a
comment explaining the choice. They happened because there was no stated rule about **which clock owns
which decision**. That rule is the table at the top of this document.

### Enforcement

`DateTime.Now` and `DateTimeOffset.Now` are **banned at compile time** by the Headless MSBuild SDK
(`BannedApiAnalyzers`, `RS0030`, via `BannedSymbols.txt`). The ban fires even inside a `<see cref="..."/>`
doc comment — reference it as `<c>DateTime.Now</c>` instead.

`DateTime.UtcNow` and `DateTimeOffset.UtcNow` are **not in `BannedSymbols.txt` and trigger no analyzer** —
enforcement there is convention and code review only. They are not simply added to the same ban because,
inside an EF `ExecuteUpdate`/`Where` expression tree, a bare `DateTime.UtcNow` is NOT evaluated in-process —
the provider translates it to server time. This is the *correct* way to express the DB clock in LINQ
(see §1), and a blanket ban on the symbol would flag the framework's own correct code — the majority of
this repo's live `DateTime.UtcNow` uses sit inside exactly this pattern in `Headless.Jobs.EntityFramework`.

Closing this gap would require either (a) adding `UtcNow` to `BannedSymbols.txt` upstream in headless-sdk
plus a documented suppression pattern for the EF expression-tree sites, or (b) a custom Roslyn analyzer
that understands expression-tree context and exempts only those sites. Neither exists yet — this is a
known gap, not a resolved one.

Outside expression trees, everything must resolve time through an injected `TimeProvider`; the one
sanctioned direct read of the system clock is `TimeProvider.System`, registered in DI as the production
implementation.

## Related

- [atomic-database-clock-relational-lease-claims.md](atomic-database-clock-relational-lease-claims.md) —
  the per-provider SQL mechanics for §1.
- `docs/solutions/architecture-patterns/coordination-register-establishes-durable-liveness.md` — the same
  store-time authority principle, applied to Coordination liveness. This was the framework's first
  statement of the rule.
- `docs/solutions/design-patterns/redis-zset-semaphore-prune-count-separation.md` — Redis server time
  inside atomic semaphore acquisition.
- [PostgreSQL date/time functions](https://www.postgresql.org/docs/current/functions-datetime.html) —
  `now()`/`CURRENT_TIMESTAMP` are transaction-start time; `clock_timestamp()` is wall-clock;
  `statement_timestamp()` is statement-start.
- [SQL Server `SYSUTCDATETIME`](https://learn.microsoft.com/sql/t-sql/functions/sysutcdatetime-transact-sql)
- [Npgsql EF translations](https://www.npgsql.org/efcore/mapping/translations.html) — confirms
  `DateTime.UtcNow` → `now()`.
