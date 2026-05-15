---
id: 2026-05-14-001
title: "Messaging P1.1: retry policy and delayed retry contract"
status: superseded
created: 2026-05-14
superseded_on: 2026-05-15
superseded_by: docs/plans/2026-05-15-001-feat-messaging-retry-revised-leans.md
issue: https://github.com/xshaheen/headless-framework/issues/229
depth: standard
origin: issue #229 (well-specified; carries final decisions and external grounding)
---

# Messaging P1.1: retry policy and delayed retry contract

> **Superseded by [docs/plans/2026-05-15-001-feat-messaging-retry-revised-leans.md](2026-05-15-001-feat-messaging-retry-revised-leans.md).**
>
> The retry contract shipped in PR #254 follows the revised plan, not this one. After PR #254's first review surface, five follow-up issues (#255-#258, #260) traced back to transition-analysis gaps in the original design — most notably the linear `MaxAttempts` model and the dual-branch pickup query. The revised plan replaces those with a multiplicative budget and a single-branch pickup query (NServiceBus/Wolverine-style).
>
> **What actually shipped (vs this plan):**
>
> | This plan said | What shipped |
> |---|---|
> | `RetryPolicyOptions.MaxAttempts` (default 50) — single linear ceiling for total attempts | **Removed.** Replaced by `MaxInlineRetries × MaxPersistedRetries`. Total = `(MaxInlineRetries+1) × (MaxPersistedRetries+1)`. Defaults: `3 × 16 = 48`. |
> | `MediumMessage.Retries` increments on every retry decision (inline + persisted) | `Retries` counts **persisted pickups only**. Inline iterations do not advance it. The increment happens at the executor call site on persist-transition; `RetryHelper` is pure w.r.t. `MediumMessage`. |
> | `OnExhausted` is `Action<FailedInfo>?` | `Func<FailedInfo, CancellationToken, Task>?` (awaited inside the live dispatch scope). |
> | Pickup query has two branches: `NextRetryAt <= now` OR `Scheduled AND NextRetryAt IS NULL` | **Collapsed to single branch**: `NextRetryAt IS NOT NULL AND NextRetryAt <= now`. The status-based fallback (and its dedicated index) is gone. |
> | Initial store leaves `NextRetryAt = NULL` | Initial store sets `NextRetryAt = UtcNow + InitialDispatchGrace` (new option, default 30s). |
> | Host-shutdown OCE writes terminal `Failed` state | Host-shutdown OCE returns without writing state; the row keeps its prior `NextRetryAt` for crash-recovery on restart. |
> | `OnExhausted` contract: framework guarantees single-fire | Documented as **at-least-once**; handler MUST be idempotent (use `Message.GetId()` as dedupe key). Aligned with MassTransit/NServiceBus/Wolverine. |
> | _(not previously planned)_ | **Defensive single-fire storage guard:** `IDataStorage.ChangePublishStateAsync` / `ChangeReceiveStateAsync` now return `ValueTask<bool>`. The UPDATE statement adds `AND StatusName NOT IN ('Succeeded','Failed')` so a redelivery of an already-terminal row reports zero affected rows; `_SetFailedState` then skips the OnExhausted invocation. This is best-effort and complements (does not replace) the at-least-once contract. |
>
> The rest of the original plan (typed `RetryPolicyOptions`, removal of scattered `MessagingOptions` primitives, `NextRetryAt` field, single shared exception classifier, `MessageNeedToRetryProcessor` driven by `NextRetryAt`) shipped intact and remains the authoritative reference for that scope.
>
> Read this document for the original design narrative and decision context; read the revised-leans plan for the final implementation surface.

## Problem Frame

The current retry model scatters retry controls across `MessagingOptions` as loosely coupled primitives (`FailedRetryCount`, `FailedRetryInterval`, `FallbackWindowLookbackSeconds`, `RetryBackoffStrategy`, `FailedThresholdCallback`). There is no cohesive type that documents the full contract, no validation, and no explicit next-attempt timestamp — the persisted retry processor uses an `Added < now - lookbackWindow` heuristic which is unreliable when inline retry loops sleep for variable durations.

The goals for this plan:
1. Replace the scattered primitives with a typed, validated `RetryPolicyOptions` class.
2. Eliminate inline sleeping for long delayed retries; instead write `NextRetryAt` to storage and let the persisted processor pick up when the field is due.
3. Centralize exception classification so both backoff strategies share one authoritative list.
4. Drop `FallbackWindowLookbackSeconds` from `IDataStorage` query contracts; replace with `NextRetryAt`-based queries.

Issues #230 (telemetry) and #231 (docs) must land **after** this — both describe the final retry contract this plan establishes.

## Scope

**In scope:**
- `RetryPolicyOptions` type with inline validator
- `MessagingOptions` — remove retry primitives, add `RetryPolicy` property
- `MessagingOptionsValidator` (new; `MessagingOptions` currently has no validator)
- `RetryExceptionClassifier` — centralized exception classification
- `MediumMessage.NextRetryAt` field
- `IDataStorage` — update `GetPublishedMessagesOfNeedRetry` / `GetReceivedMessagesOfNeedRetry` signatures; add `NextRetryAt` to `ChangePublishStateAsync` / `ChangeReceiveStateAsync`
- All storage providers: `PostgreSqlDataStorage`, `SqlServerDataStorage`, `InMemoryStorage`
- `SubscribeExecutor` and `MessageSender` — extract shared retry helper, drive from `RetryPolicyOptions`, write `NextRetryAt`
- `MessageNeedToRetryProcessor` — drop `_lookbackWindow`, use `NextRetryAt`-based query
- DB migration for all providers that add the `NextRetryAt` column
- Tests for all changed units

**Out of scope:**
- Changing the retry algorithm itself (exponential vs fixed) — `IRetryBackoffStrategy` stays as-is
- Telemetry / metrics for retry events — #230
- End-user documentation — #231
- Circuit-breaker changes
- Transports — core owns all retry/backoff, transports must not reinvent (see `docs/solutions/guides/messaging-transport-provider-guide.md`)

## Key Decisions

| Decision | Rationale |
|---|---|
| `RetryPolicyOptions` is a nested class on `MessagingOptions` (or lives at `Configuration/RetryPolicyOptions.cs`) | Keeps the messaging configuration namespace clean; mirrors `CircuitBreakerOptions` pattern |
| `MaxAttempts` = total attempts including original execution | Matches MassTransit, NServiceBus, Wolverine semantics; `MaxAttempts = 1` means no retry |
| `MaxInlineRetries` controls the do/while loop bound; `MaxAttempts - 1 - MaxInlineRetries` attempts go to persisted retry | Inline retries are fast/cheap; persisted retries handle long backoffs without blocking a thread |
| `NextRetryAt` written to storage when an inline-retry attempt exhausts and remaining attempts > 0 | Explicit scheduled timestamp removes the `Added+lookback` heuristic entirely |
| `IRetryBackoffStrategy` kept as public NuGet contract | Breaking it would affect consumers of `Headless.Messaging.Abstractions` |
| Exception classification centralized in `RetryExceptionClassifier` | Both `ExponentialBackoffStrategy` and `FixedIntervalBackoffStrategy` had an identical hardcoded list — single source of truth |
| `IDataStorage.GetPublishedMessagesOfNeedRetry` / `GetReceivedMessagesOfNeedRetry` — drop `TimeSpan lookbackSeconds` param, replace with `NextRetryAt <= now` query in providers | Parameter was a band-aid over the missing `NextRetryAt` field |
| `ChangePublishStateAsync` / `ChangeReceiveStateAsync` — add `DateTime? nextRetryAt = null` optional parameter | Preserves existing call sites; only retry transition code passes a value |
| `MessagingOptions` gets its own validator (`MessagingOptionsValidator`) | Currently the only top-level options class without one |
| `FailedThresholdCallback` → `OnExhausted` on `RetryPolicyOptions` | Clearer name, lives next to the policy it relates to |
| `MaxInlineRetries` is a **count** (not a `TimeSpan` threshold like `InlineRetryThreshold`) | Mirrors MassTransit's `Immediate(n)` and Wolverine's `RetryWithCooldown(n, ...)` semantics — operators reason about retry budgets in attempt counts more often than in time. A time-based threshold defers the inline/persisted split to runtime delay computation, which makes test isolation harder and couples the knob to the configured `BackoffStrategy`. If user feedback shows operators want time-based, a future `InlineRetryThreshold` can be added orthogonally. |
| Five `MessagingOptions` retry primitives removed in a single cut (no `[Obsolete]` deprecation cycle) | Project is pre-1.0 greenfield (see `CLAUDE.md`: 'Prefer simpler, cleaner APIs even when that requires breaking changes'). The cleanup payoff (one cohesive type, no documentation drift between primitives and policy) dominates the adoption-friction cost. If real external consumers surface before release, revisit with a one-release deprecation window. |
| `FailedInfo.Exception` retained (not removed) | The exception that caused the final failure is the single most useful signal for the dominant `OnExhausted` use case (log/alert/DLQ enrichment). MassTransit, NServiceBus, and Wolverine all expose it in their equivalent terminal-handler types. Re-adding it as a required-init property costs one line and removes a log-correlation tax from every consumer. |

## Implementation Units

### U1 — `RetryPolicyOptions` and `MessagingOptions` wiring

**Files:**
- `src/Headless.Messaging.Core/Configuration/RetryPolicyOptions.cs` (new)
- `src/Headless.Messaging.Core/Configuration/MessagingOptions.cs` (modify)
- `src/Headless.Messaging.Core/Setup.cs` (modify — update validator wire-up and remove retired retry primitives from registration)

**What to do:**

Create `RetryPolicyOptions.cs`:

```csharp
// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Configuration;

[PublicAPI]
public sealed class RetryPolicyOptions
{
    /// <summary>
    /// Total number of delivery attempts, including the first (non-retry) execution.
    /// Setting this to 1 disables retry entirely. Default: 50 (matches previous FailedRetryCount=50 behavior:
    /// old code incremented Retries on each failure and stopped at Retries >= 50, giving 50 total attempts).
    /// </summary>
    public int MaxAttempts { get; set; } = 50;

    /// <summary>
    /// Maximum number of retries to run inline (in-process, with short delay) before
    /// persisting the message for a later delayed attempt. Default: 2.
    /// </summary>
    public int MaxInlineRetries { get; set; } = 2;

    /// <summary>
    /// Backoff strategy used to compute per-attempt delay. Defaults to exponential backoff.
    /// </summary>
    public IRetryBackoffStrategy BackoffStrategy { get; set; } = new ExponentialBackoffStrategy();

    /// <summary>
    /// Called once when all retry attempts are exhausted. Receives the final failure info.
    /// </summary>
    public Action<FailedInfo>? OnExhausted { get; set; }
}

internal sealed class RetryPolicyOptionsValidator : AbstractValidator<RetryPolicyOptions>
{
    public RetryPolicyOptionsValidator()
    {
        RuleFor(x => x.MaxAttempts).GreaterThanOrEqualTo(1);
        RuleFor(x => x.MaxInlineRetries).GreaterThanOrEqualTo(0);
        RuleFor(x => x.MaxInlineRetries)
            .LessThan(x => x.MaxAttempts)
            .WithMessage("MaxInlineRetries must be less than MaxAttempts.");
        RuleFor(x => x.BackoffStrategy).NotNull();
    }
}
```

Modify `MessagingOptions.cs`:
- Add `public RetryPolicyOptions RetryPolicy { get; set; } = new();`
- Remove: `FailedRetryCount`, `FailedRetryInterval`, `FallbackWindowLookbackSeconds`, `RetryBackoffStrategy`, `FailedThresholdCallback`

**Worked example — how `MaxAttempts` and `MaxInlineRetries` combine:**

| `MaxAttempts` | `MaxInlineRetries` | Original | Inline retries | Persisted retries | Total attempts |
|---|---|---|---|---|---|
| 1 | 0 | 1 | 0 | 0 | 1 (no retry at all) |
| 1 | 0..N | 1 | 0 | 0 | 1 (validator requires `MaxInlineRetries < MaxAttempts`) |
| 3 | 2 | 1 | 2 | 0 | 3 |
| 10 | 2 | 1 | 2 | 7 | 10 |
| 50 | 2 (default) | 1 | 2 | 47 | 50 |
| 50 | 0 | 1 | 0 | 49 | 50 (every retry goes through the persisted processor) |

The derived "persisted retries" count is `MaxAttempts - 1 - MaxInlineRetries`. The validator enforces `MaxInlineRetries < MaxAttempts` so this never goes negative.

Add `MessagingOptionsValidator` (new; currently `MessagingOptions` has no validator):

```csharp
internal sealed class MessagingOptionsValidator : AbstractValidator<MessagingOptions>
{
    public MessagingOptionsValidator()
    {
        RuleFor(x => x.RetryPolicy).NotNull().SetValidator(new RetryPolicyOptionsValidator());
        // ... any cross-option invariants per the institutional learning:
        // cross-option invariants go in FluentValidation, not runtime compensation
        // (see docs/solutions/concurrency/startup-pause-gating-and-half-open-recovery.md)
    }
}
```

Wire the validator in `SetupApiServices.cs` / wherever `MessagingOptions` is registered — use `services.Configure<MessagingOptions, MessagingOptionsValidator>(action)` consistent with Headless.Hosting pattern.

**`RetryProcessorOptionsValidator` re-registration:** Currently `RetryProcessorOptionsValidator` injects `IOptions<MessagingOptions>` to read `FailedRetryInterval`. After U5 removes that constructor parameter, the existing DI registration in `Setup.cs` (likely `services.AddSingleton<IValidator<RetryProcessorOptions>, RetryProcessorOptionsValidator>()` or `services.Configure<RetryProcessorOptions, RetryProcessorOptionsValidator>(...)`) must be re-checked: ensure the validator is registered without resolving `IOptions<MessagingOptions>` as a constructor dependency.

**Test file:** `tests/Headless.Messaging.Core.Tests.Unit/Configuration/MessagingOptionsValidationTests.cs` (extend existing)

**Test scenarios:**
- `MaxAttempts = 0` fails validation
- `MaxAttempts = 1` passes (no retries)
- `MaxInlineRetries >= MaxAttempts` fails validation
- `BackoffStrategy = null` fails validation
- Default values produce valid options
- `RetryPolicy = null` on `MessagingOptions` fails validation

---

### U2 — Centralized exception classification (`RetryExceptionClassifier`)

**Files:**
- `src/Headless.Messaging.Core/Retry/RetryExceptionClassifier.cs` (new)
- `src/Headless.Messaging.Core/Retry/ExponentialBackoffStrategy.cs` (modify — remove duplicated list, delegate)
- `src/Headless.Messaging.Core/Retry/FixedIntervalBackoffStrategy.cs` (modify — same)

**What to do:**

Extract the hardcoded permanent-exception type check (currently duplicated in both strategies) into a single static classifier:

```csharp
// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Retry;

/// <summary>
/// Classifies exceptions by their retry behavior.
/// Centralizes the decision that was previously duplicated in each backoff strategy.
/// </summary>
internal static class RetryExceptionClassifier
{
    /// <summary>
    /// Returns true for exceptions that represent permanent failures — no retry should occur.
    /// </summary>
    public static bool IsPermanent(Exception exception) => exception is
        SubscriberNotFoundException
        or ArgumentNullException
        or ArgumentException
        or InvalidOperationException
        or NotSupportedException;
}
```

Update `ExponentialBackoffStrategy.ShouldRetry` and `FixedIntervalBackoffStrategy.ShouldRetry` to delegate: `return !RetryExceptionClassifier.IsPermanent(exception);`

**Important:** `RetryExceptionClassifier` is for internal use by the default backoff strategies only. It must NOT be used in `RetryHelper` — see U4 for why `RetryHelper` must call `strategy.ShouldRetry()` directly to preserve custom strategy behavior.

**Test file:** `tests/Headless.Messaging.Core.Tests.Unit/Retry/RetryExceptionClassifierTests.cs` (new)

**Test scenarios (for new classifier):**
- Each permanent exception type returns `IsPermanent = true`
- Generic `Exception` returns `IsPermanent = false`
- `OperationCanceledException` returns `IsPermanent = false` (cancellation is separate)
- Subclasses of permanent types (e.g., custom `ArgumentException` subclass) return `IsPermanent = true`

**Sibling test files to update:**
- `ExponentialBackoffStrategyTests.cs` — remove duplicate permanent-exception test cases if they now test the delegated path
- `FixedIntervalBackoffStrategyTests.cs` — same

---

### U3 — Storage: `NextRetryAt` field and `IDataStorage` contract update

**Files:**
- `src/Headless.Messaging.Core/Messages/MediumMessage.cs` (modify — add `NextRetryAt` property)
- `src/Headless.Messaging.Core/Persistence/IDataStorage.cs` (modify — signature changes per below)
- `src/Headless.Messaging.PostgreSql/PostgreSqlDataStorage.cs` (modify — query change, schema column, `StoreReceivedExceptionMessageAsync` switch from `FailedRetryCount` to `MaxAttempts`)
- `src/Headless.Messaging.SqlServer/SqlServerDataStorage.cs` (modify — same set as PostgreSQL)
- `src/Headless.Messaging.InMemory/InMemoryStorage.cs` (modify — filter rewrite, `IOptions<MessagingOptions>` injection, `StoreReceivedExceptionMessageAsync` switch)

**What to do:**

`MediumMessage.cs` — add field:
```csharp
public DateTime? NextRetryAt { get; set; }
```

`IDataStorage.cs`:
1. Drop `TimeSpan lookbackSeconds` parameter from both retry-query methods:
   ```csharp
   ValueTask<IEnumerable<MediumMessage>> GetPublishedMessagesOfNeedRetry(CancellationToken cancellationToken = default);
   ValueTask<IEnumerable<MediumMessage>> GetReceivedMessagesOfNeedRetry(CancellationToken cancellationToken = default);
   ```
2. Add `DateTime? nextRetryAt = null` to `ChangePublishStateAsync` and `ChangeReceiveStateAsync`:
   ```csharp
   ValueTask ChangePublishStateAsync(MediumMessage message, StatusName state, object? transaction = null,
       DateTime? nextRetryAt = null, CancellationToken cancellationToken = default);
   ValueTask ChangeReceiveStateAsync(MediumMessage message, StatusName state,
       DateTime? nextRetryAt = null, CancellationToken cancellationToken = default);
   ```
   **Semantics:** When `nextRetryAt = null`, providers must clear the persisted `NextRetryAt` column (write `NULL`). This is required on `StatusName.Succeeded` transitions so a previously-scheduled retry timestamp does not linger and cause spurious re-pickup if the row's status is ever re-read. Passing a value sets that value; passing `null` writes `NULL`. There is no "leave as-is" option.

**Status lifecycle (which `(StatusName, NextRetryAt)` combinations the polling query picks up):**

| `StatusName` | `NextRetryAt` | Meaning | Picked by poll? |
|---|---|---|---|
| `Scheduled` | `NULL` | Initial state on `StoreMessageAsync` / `StoreReceivedMessageAsync`. If the process crashes before the first dispatch attempt, the message stays here. | **Yes** — crash-recovery branch |
| `Scheduled` | non-NULL | Not produced by this design (initial store never sets a timestamp; any retry transition writes `Failed`). Reserved for future use. | n/a |
| `Failed` | non-NULL, `<= now()` | Inline retries exhausted, message scheduled for persisted retry at the timestamp. | **Yes** — primary branch |
| `Failed` | non-NULL, `> now()` | Scheduled for a future retry; not yet due. | No |
| `Failed` | `NULL` | Terminal: either `RetryDecision.Stop` (permanent failure, `OnExhausted` not fired) or `RetryDecision.Exhausted` (`OnExhausted` fired). Both transitions write `NULL` per the U3 clear-on-null semantics. | **No** — neither branch matches |
| `Succeeded` | `NULL` (cleared on success transition) | Terminal success. | No |

**Storage provider query change:**

Current PostgreSQL query:
```sql
WHERE "Retries" < @Retries AND "Added" < @Added AND "StatusName" IN ('Failed','Scheduled')
LIMIT 200 FOR UPDATE SKIP LOCKED
```

Replace with (two-branch filter — handles scheduled crash-recovery without initializing NextRetryAt on store):
```sql
WHERE (
    ("NextRetryAt" IS NOT NULL AND "NextRetryAt" <= now())
    OR  ("StatusName" = 'Scheduled' AND "NextRetryAt" IS NULL)
)
LIMIT 200 FOR UPDATE SKIP LOCKED
```

Same pattern for SqlServer (`GETUTCDATE()` instead of `now()`).

**Schema:** Add `NextRetryAt TIMESTAMPTZ NULL` (PostgreSQL) / `NextRetryAt DATETIME2 NULL` (SqlServer) to the table creation scripts (not migrations — greenfield).

**Indexing (two partial indexes, one per query branch):** A single composite index on `(StatusName, NextRetryAt)` does not serve the first OR branch (no `StatusName` predicate). The planner can also disable bitmap-OR under `FOR UPDATE SKIP LOCKED` in some versions, falling back to a seq scan. Create one partial index per branch so each is cleanly served:

```sql
-- PostgreSQL
CREATE INDEX ix_published_next_retry      ON published      ("NextRetryAt") WHERE "NextRetryAt" IS NOT NULL;
CREATE INDEX ix_published_scheduled_null  ON published      ("StatusName")  WHERE "StatusName" = 'Scheduled' AND "NextRetryAt" IS NULL;
-- Same pair on the received table.
```

SqlServer equivalents use filtered indexes (`WHERE ... IS NOT NULL`, `WHERE StatusName = 'Scheduled' AND NextRetryAt IS NULL`). Add an `EXPLAIN ANALYZE` (PostgreSQL) / actual-execution-plan (SqlServer) check to the integration test suite confirming both partial indexes are used.

**Npgsql `DateTime` kind discipline:** Npgsql 6+ maps `timestamptz` ↔ `DateTime` with `DateTimeKind.Utc` only. The `MediumMessage.NextRetryAt` property is `DateTime?`; all callers (`RetryHelper` / `SubscribeExecutor` / `MessageSender`) must use `DateTime.UtcNow.Add(...)` — never `DateTime.Now` or unspecified-kind values — or Npgsql will throw at write time. The SQL comparison uses `now()` (PostgreSQL UTC equivalent for `timestamptz`) / `GETUTCDATE()` (SqlServer); this is consistent with UTC values from the application.

**`Retries` increment ownership:** `ChangePublishStateAsync` and `ChangeReceiveStateAsync` must write `message.Retries` verbatim (passthrough). `RetryHelper.ComputeRetryDecision` owns the increment (`message.Retries++`) before calling `ChangeReceiveStateAsync`; providers must not apply their own `Retries + 1` SQL expression or double-counting will occur.

**Lock-step dependency with U5:** The `IDataStorage` signature change (dropping `lookbackSeconds`) must land in the same PR as the `_GetSafelyAsync` change in `IProcessor.NeedRetry.cs` (U5). The two are tightly coupled — splitting them across PRs leaves the build broken.

**`StoreReceivedExceptionMessageAsync` retry-count source:** `StoreReceivedExceptionMessageAsync` (and any storage method that seeds `Retries` from configuration) currently reads `MessagingOptions.FailedRetryCount`. Update these call-sites to read `RetryPolicyOptions.MaxAttempts` (via `IOptions<MessagingOptions>.Value.RetryPolicy.MaxAttempts`) so the exception-store path stays consistent with the new policy.

**InMemoryStorage:** Filter `((message.NextRetryAt != null && message.NextRetryAt <= DateTime.UtcNow) || (message.StatusName == StatusName.Scheduled && message.NextRetryAt == null)) && message.Retries < retryPolicy.MaxAttempts`. The `Retries < MaxAttempts` clause mirrors the persisted providers' previous `WHERE "Retries" < @Retries` guard — without it, a message that exhausted attempts but failed to transition off Scheduled would loop. Inject `IOptions<MessagingOptions>` into `InMemoryStorage` to read `MaxAttempts`, or accept it via the existing options the storage already resolves.

**Test file:** No dedicated unit test for storage queries (those are integration tests). Update integration tests in `tests/Headless.Messaging.*.Tests.Integration/` for the retry-query path.

**Test scenarios (unit — InMemoryStorage or mock-based):**
- Message with `NextRetryAt = null` is not returned by `GetReceivedMessagesOfNeedRetry`
- Message with `NextRetryAt > now` is not returned
- Message with `NextRetryAt <= now` is returned
- `ChangeReceiveStateAsync` with `nextRetryAt` sets the field on the message in storage

---

### U4 — First-level (inline) retry refactor

**Files:**
- `src/Headless.Messaging.Core/Internal/ISubscribeExecutor.cs` (modify — update `using Headless.Messaging.Internal;` → `using Headless.Messaging.Retry;` for `RetryDecision`)
- `src/Headless.Messaging.Core/Internal/IMessageSender.cs` (modify — same `using` update)
- `src/Headless.Messaging.Core/Internal/RetryDecision.cs` (**delete** — replaced by the relocated/reshaped type below)
- `src/Headless.Messaging.Core/Retry/RetryHelper.cs` (new — extracted shared logic)
- `src/Headless.Messaging.Core/Retry/RetryDecision.cs` (**relocate + reshape** from `Internal/RetryDecision.cs`)

**Relocation note:** The existing `Internal/RetryDecision.cs` is a `record struct RetryDecision(bool ShouldRetry, TimeSpan Delay)` with `static Stop` and `static Continue(TimeSpan)`. The new shape (below) keeps the same call surface (`RetryDecision.Stop`, `RetryDecision.Continue(delay)`, `.ShouldRetry`) but adds the third state `Exhausted`. Existing call sites in `ISubscribeExecutor.cs` and `IMessageSender.cs` continue to compile after the `using` swap — only the namespace changes for those two existing factories. The new `Exhausted` factory is only referenced from `RetryHelper.ComputeRetryDecision`.

**`RetryDecision` type:** A simple internal struct with three states. `Stop` and `Exhausted` are stateless (no delay); `Continue` carries the computed `TimeSpan` delay.

```csharp
// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Retry;

internal readonly record struct RetryDecision
{
    public enum Kind { Stop, Exhausted, Continue }

    public Kind Outcome { get; init; }
    public TimeSpan Delay { get; init; }

    public static RetryDecision Stop { get; } = new() { Outcome = Kind.Stop };
    public static RetryDecision Exhausted { get; } = new() { Outcome = Kind.Exhausted };
    public static RetryDecision Continue(TimeSpan delay) => new() { Outcome = Kind.Continue, Delay = delay };

    public bool ShouldRetry => Outcome == Kind.Continue;
}
```

The caller dispatches on `Outcome` (or uses the `ShouldRetry` shortcut). `==`/`!=` work because of `record struct` semantics, which is what the U4 inline-retry sketch relies on (`if (retryDecision == RetryDecision.Stop)`).

**What to do:**

The `_UpdateMessageForRetry` decision tree is identical in both `SubscribeExecutor` and `MessageSender`. Extract it:

```csharp
// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Retry;

/// <summary>
/// Shared retry decision logic used by both the consume path (SubscribeExecutor)
/// and the publish/outbox path (MessageSender).
/// </summary>
internal static class RetryHelper
{
    public static RetryDecision ComputeRetryDecision(
        MediumMessage message,
        Exception exception,
        RetryPolicyOptions policy,
        bool isCancellation)
    {
        if (isCancellation)
        {
            return RetryDecision.Stop;
        }

        // Delegate to the configured strategy — preserves custom IRetryBackoffStrategy.ShouldRetry() behavior.
        // Do NOT use RetryExceptionClassifier here; that is only for internal default strategy implementations.
        if (!policy.BackoffStrategy.ShouldRetry(exception))
        {
            return RetryDecision.Stop; // permanent failure — no callback
        }

        message.Retries++;
        var remainingAttempts = policy.MaxAttempts - message.Retries;

        if (remainingAttempts <= 0)
        {
            // RetryHelper does not construct FailedInfo — it lacks the context it needs (scoped
            // IServiceProvider, MessageType). The caller owns construction and invokes OnExhausted
            // with the actual FailedInfo shape (see note below).
            return RetryDecision.Exhausted;
        }

        var delay = policy.BackoffStrategy.GetNextDelay(message.Retries, exception);
        if (delay is null)
        {
            return RetryDecision.Exhausted; // strategy signalled stop; caller invokes OnExhausted
        }

        return RetryDecision.Continue(delay.Value);
    }

    /// <summary>
    /// Unified cancellation predicate. Both SubscribeExecutor and MessageSender MUST use this
    /// helper to compute the <paramref name="isCancellation"/> argument so the Stop-vs-Exhausted
    /// decision (and therefore OnExhausted firing) does not drift between call sites.
    /// </summary>
    public static bool IsCancellation(Exception ex, CancellationToken ct) =>
        ex is OperationCanceledException oce
        && (oce.CancellationToken == ct || ct.IsCancellationRequested);
}
```

**Inline retry loop change (both `SubscribeExecutor` and `MessageSender`):**

The do/while loop currently runs until `!ShouldRetry`. Replace with:

```csharp
// Directional sketch — not implementation specification
var inlineRetries = 0;
do
{
    var (decision, result) = await _ExecuteWithoutRetryAsync(message, descriptor, ct);
    if (result == OperateResult.Success) return result;

    var retryDecision = RetryHelper.ComputeRetryDecision(message, decision.Exception, retryPolicy, isCancellation: ...);
    switch (retryDecision.Outcome)
    {
        case RetryDecision.Kind.Stop:
            // Permanent failure (e.g. SubscriberNotFoundException). Set Failed explicitly so the persisted
            // retry processor does not re-pick a Scheduled message with NextRetryAt = NULL (infinite loop).
            await _dataStorage.ChangeReceiveStateAsync(message, StatusName.Failed, cancellationToken: ct);
            return result;
        case RetryDecision.Kind.Exhausted:
            await _dataStorage.ChangeReceiveStateAsync(message, StatusName.Failed, cancellationToken: ct);
            policy.OnExhausted?.Invoke(new FailedInfo { /* see FailedInfo shape note above */ });
            return result;
        // Kind.Continue falls through to the inline-budget / persisted-schedule logic below
    }

    inlineRetries++;
    if (inlineRetries < retryPolicy.MaxInlineRetries)
    {
        // Still within inline budget — sleep briefly and loop
        await Task.Delay(retryDecision.Delay, cancellationToken);
        continue;
    }

    // Inline budget exhausted — schedule for persisted retry
    var nextRetryAt = DateTime.UtcNow.Add(retryDecision.Delay);
    await _dataStorage.ChangeReceiveStateAsync(message, StatusName.Failed, nextRetryAt: nextRetryAt, cancellationToken: ct);
    return result;
} while (true);
```

**`FailedInfo` shape (for caller use):** `FailedInfo` has no positional constructor and no `Exception` property. Callers (`SubscribeExecutor`, `MessageSender`) must construct it using required-property initialization:

```csharp
policy.OnExhausted?.Invoke(new FailedInfo
{
    ServiceProvider = <scoped IServiceProvider>,  // from the active DI scope
    MessageType = MessageType.Subscribe,          // or Publish — from context
    Message = <Message>,                          // Message, not MediumMessage
    Exception = <Exception>,                      // the exception that triggered exhaustion
});
```

**`FailedInfo` shape addition (U4 also updates `src/Headless.Messaging.Core/Messages/FailedInfo.cs`):** Add `public required Exception Exception { get; init; }` to `FailedInfo`. The exception is the primary signal for the typical `OnExhausted` workflow (logging, alerting, DLQ enrichment); without it consumers must correlate logs across the framework's catch block and the handler's invocation.

`RetryHelper.ComputeRetryDecision` returns `RetryDecision.Exhausted` when `OnExhausted` should fire; the caller is responsible for the invocation.

**`OnExhausted` semantics — permanent vs exhausted:** `OnExhausted` fires only on `RetryDecision.Exhausted` (attempts ran out, or strategy returned `null` delay). It does NOT fire on `RetryDecision.Stop` (permanent exception like `SubscriberNotFoundException`, or cancellation). Rationale: `OnExhausted` is for terminal-handler escalation after exhausting the retry budget; permanent failures are different — they short-circuit the budget entirely and represent a contract/dispatch problem, not a transient error. Document this in the `OnExhausted` XML doc comment in `RetryPolicyOptions.cs`.

**`OnExhausted` scope-lifetime contract:** Callers MUST invoke `OnExhausted` synchronously inside the same DI scope they used to dispatch the message — `FailedInfo.ServiceProvider` is that live scope. In `SubscribeExecutor` this falls out naturally (the consume scope wraps the whole dispatch). In `MessageSender` the background-processor scope is created per send attempt; the `OnExhausted` invocation must occur BEFORE the `using`/`await using` scope-disposal block exits. A user handler that does `serviceProvider.GetRequiredService<T>()` inside `OnExhausted` will throw `ObjectDisposedException` if this ordering is violated. Document this in the `OnExhausted` XML doc and pin the contract with a `MessageSender` test scenario: 'OnExhausted handler can resolve a scoped service from `FailedInfo.ServiceProvider`'.

`MessageSender` uses `CancellationToken.None` for its delay (intentional — outbox send is non-cancellable mid-retry); keep that as-is.

**Test files:**
- `tests/Headless.Messaging.Core.Tests.Unit/SubscribeExecutorRetryTests.cs` (extend)
- `tests/Headless.Messaging.Core.Tests.Unit/MessageSenderTests.cs` (extend)
- `tests/Headless.Messaging.Core.Tests.Unit/Retry/RetryHelperTests.cs` (new)

**Test scenarios (`RetryHelper`):**
- Cancellation exception → Stop (no callback, no increment)
- Permanent exception → Stop (no callback)
- Retries exhausted (Retries == MaxAttempts) → Exhausted + OnExhausted called
- `GetNextDelay` returns null → Exhausted + OnExhausted called
- Normal exception within budget → Continue with correct delay
- `MaxAttempts = 1` with permanent exception → Stop (no callback); same config with transient exception → Exhausted (callback fires once)
- **Equality discriminator regression test:** `RetryDecision.Stop != RetryDecision.Exhausted` AND `RetryDecision.Stop != RetryDecision.Continue(TimeSpan.Zero)`. Pins the invariant so future `Kind` additions do not silently collide on the `Delay = Zero` default.

**Test scenarios (`SubscribeExecutor` inline retry):**
- Success on first attempt — no retry
- Failure × N, success on N+1 within `MaxInlineRetries` — succeeds, no `NextRetryAt` written
- Failure × (MaxInlineRetries + 1) — `NextRetryAt` written to storage, message stays Failed
- `MaxInlineRetries = 0` — first failure immediately schedules persisted retry (no inline sleep at all); validates the boundary that disables inline retries entirely
- `MaxAttempts = 1, MaxInlineRetries = 0` — first failure goes straight to `Exhausted` (no retry possible), `OnExhausted` fires once. Validator constraint `MaxInlineRetries < MaxAttempts` requires `MaxInlineRetries = 0` here.
- Permanent exception — stops immediately, no persisted retry
- Cancellation — stops cleanly, no callback

**Test scenarios (`MessageSender`):**
- Same inline retry shape as above; verify `CancellationToken.None` used for delay
- `OnExhausted` handler can resolve a scoped service from `FailedInfo.ServiceProvider` — pins the scope-lifetime contract (handler runs inside the still-live send scope)

---

### U5 — Second-level (persisted) retry: `MessageNeedToRetryProcessor` update

**Files:**
- `src/Headless.Messaging.Core/Configuration/RetryProcessorOptions.cs` (modify — add `BaseInterval`)
- `src/Headless.Messaging.Core/Processor/IProcessor.NeedRetry.cs` (modify)

**What to do:**

Add `BaseInterval` to `RetryProcessorOptions` (replaces `FailedRetryInterval` from `MessagingOptions`):

```csharp
/// <summary>
/// Base polling interval for the retry processor. Defaults to 60 seconds (matches previous FailedRetryInterval default).
/// </summary>
public TimeSpan BaseInterval { get; set; } = TimeSpan.FromSeconds(60);
```

`RetryProcessorOptionsValidator` currently injects `IOptions<MessagingOptions>` to read `FailedRetryInterval` and validate `MaxPollingInterval >= FailedRetryInterval`. After adding `BaseInterval`, this cross-type dependency can be removed — validate `MaxPollingInterval >= BaseInterval` locally instead:

```csharp
// Before (must change):
// RuleFor(x => x.MaxPollingInterval).GreaterThanOrEqualTo(failedRetryInterval)...

// After:
RuleFor(x => x.MaxPollingInterval)
    .GreaterThanOrEqualTo(x => x.BaseInterval)
    .WithMessage("MaxPollingInterval must be >= BaseInterval.");
```

Drop the `IOptions<MessagingOptions>` constructor parameter from `RetryProcessorOptionsValidator`.

`MessageNeedToRetryProcessor` constructor currently reads `_baseInterval = TimeSpan.FromSeconds(options.Value.FailedRetryInterval)`. Update to `_baseInterval = retryOptions.Value.BaseInterval`. The `IOptions<MessagingOptions>` constructor parameter can be dropped from `MessageNeedToRetryProcessor` if it is no longer needed for any other field; otherwise leave it but stop reading `FailedRetryInterval`.

Remaining changes in `MessageNeedToRetryProcessor`:
1. `_lookbackWindow` field and `_CheckSafeOptionsSet` warning are removed
2. Calls become `GetPublishedMessagesOfNeedRetry(ct)` / `GetReceivedMessagesOfNeedRetry(ct)` — no lookback param
3. `_GetSafelyAsync` helper signature simplifies: remove the `Func<TimeSpan, CancellationToken, ...>` overload, replace with `Func<CancellationToken, ...>`

**Test file:** `tests/Headless.Messaging.Core.Tests.Unit/Processor/MessageNeedToRetryProcessorTests.cs` (if it exists — extend; otherwise create)

**Test scenarios:**
- Processor passes no lookback to storage calls
- Messages with `NextRetryAt <= now` are enqueued for dispatch
- Messages with `NextRetryAt > now` are not returned (storage responsibility, but verify integration path)
- Adaptive polling still adjusts on circuit-open rate (no regression)
- `RetryProcessorOptionsValidator`: `MaxPollingInterval < BaseInterval` fails validation; no `IOptions<MessagingOptions>` dependency

---

## Breaking Changes

`IDataStorage` is a public interface. Any custom storage provider must update:
- `GetPublishedMessagesOfNeedRetry(TimeSpan, CT)` → `GetPublishedMessagesOfNeedRetry(CT)`
- `GetReceivedMessagesOfNeedRetry(TimeSpan, CT)` → `GetReceivedMessagesOfNeedRetry(CT)`
- `ChangePublishStateAsync` and `ChangeReceiveStateAsync` to accept `DateTime? nextRetryAt`

`MessagingOptions` is a public configuration type. Consumers that referenced the following properties must migrate:
- `FailedRetryCount` → `RetryPolicy.MaxAttempts`
- `FailedRetryInterval` → `RetryProcessorOptions.BaseInterval`
- `FallbackWindowLookbackSeconds` → **removed** (the new `NextRetryAt`-based query has no lookback)
- `RetryBackoffStrategy` → `RetryPolicy.BackoffStrategy`
- `FailedThresholdCallback` → `RetryPolicy.OnExhausted` (semantic change: now only fires on exhaustion, not permanent failure)

`FailedInfo` is a public type. Consumers must update:
- No positional constructor — use required-property initialization: `new FailedInfo { ServiceProvider = ..., MessageType = ..., Message = ..., Exception = ... }`
- `Exception` is now a required-init property carrying the exception that triggered exhaustion (replaces the prior implicit reliance on log-correlation)

Add to release notes as a breaking change for storage provider authors, `MessagingOptions` consumers, and `OnExhausted` handler implementers.

## Sequencing

Work can proceed in this order; each unit is independently mergeable once its dependencies are done:

```
U1 (RetryPolicyOptions) ──┐
                          ├──► U3 (storage) ──┬──► U4 (inline retry refactor)
U2 (classifier) ──────────┘                   └──► U5 (persisted processor)

Dependency detail:
- U1 (RetryPolicyOptions): no deps; do in parallel with U2
- U2 (classifier): no deps; do in parallel with U1
- U3 (storage): depends on U1 (needs RetryPolicyOptions for NextRetryAt semantics)
- U4 (inline retry refactor): depends on U1 (RetryPolicyOptions), U2 (classifier — for default strategies), U3 (NextRetryAt write path)
- U5 (persisted processor): depends on U3 (NextRetryAt-based query); independently merges with U4. **Not** dependent on U1 — `RetryProcessorOptions` (where `BaseInterval` lives) and `RetryPolicyOptions` (the inline-retry contract) are orthogonal config types.
```

Suggested merge order: U2 → U1 → U3 → U4+U5 (can be one PR if small)

## Acceptance Criteria

- [ ] Public retry configuration no longer references `FailedRetryCount` / `FailedRetryInterval` / `FallbackWindowLookbackSeconds` semantics.
- [ ] `RetryPolicyOptions` has a FluentValidation validator wired via `services.Configure<..., ...Validator>()`.
- [ ] `MessagingOptions` has its own validator.
- [ ] Exception classification is in one place (`RetryExceptionClassifier`); both backoff strategies delegate to it.
- [ ] `MediumMessage.NextRetryAt` exists; all providers persist and query it.
- [ ] `IDataStorage.GetPublishedMessagesOfNeedRetry` / `GetReceivedMessagesOfNeedRetry` have no `lookbackSeconds` parameter.
- [ ] `ChangePublishStateAsync` / `ChangeReceiveStateAsync` accept `DateTime? nextRetryAt`.
- [ ] `SubscribeExecutor` and `MessageSender` share `RetryHelper.ComputeRetryDecision`.
- [ ] After `MaxInlineRetries` inline failures are exhausted, `NextRetryAt` is written to storage and the persisted processor dispatches the message; no thread-blocking sleep occurs for the delayed retry.
- [ ] `MessagingOptions` no longer exposes `FailedRetryCount`, `FailedRetryInterval`, `FallbackWindowLookbackSeconds`, `RetryBackoffStrategy`, or `FailedThresholdCallback` (replaced per Breaking Changes).
- [ ] `MessageNeedToRetryProcessor` no longer reads `FallbackWindowLookbackSeconds`; uses `NextRetryAt`-based query.
- [ ] All existing retry tests pass; new tests cover the scenarios enumerated per unit.
- [ ] Schema creation scripts for PostgreSQL and SqlServer include `NextRetryAt` column and index.

## Risks and Deferred Questions

| Risk | Mitigation |
|---|---|
| `ChangePublishStateAsync` / `ChangeReceiveStateAsync` signature change breaks any custom `IDataStorage` implementor | Document as breaking change; use optional parameter to ease transition |
| `MaxInlineRetries` default (2) may feel different from the old behavior | Communicate the semantics change in release notes: previously all 50 retries slept inline; now only first 2 do |
| `RetryProcessorOptions.BaseInterval` (the polling interval) is currently derived from `FailedRetryInterval` — needs a new source after `FailedRetryInterval` is removed | Resolved in U5: `BaseInterval = TimeSpan.FromSeconds(60)` added to `RetryProcessorOptions`; validator updated to validate `MaxPollingInterval >= BaseInterval` locally |
| **Crash-recovery gap**: `Scheduled` messages (initial state, `NextRetryAt = NULL`) won't be picked up by the new `NextRetryAt IS NOT NULL` query if the process crashes before the initial dispatch attempt | **Resolved — option (b) chosen.** The query uses a two-branch filter: `(NextRetryAt IS NOT NULL AND NextRetryAt <= now()) OR (StatusName = 'Scheduled' AND NextRetryAt IS NULL)`. This avoids writing `NextRetryAt` on initial store (no happy-path noise) while still recovering crash-stranded `Scheduled` messages. `StoreMessageAsync` / `StoreReceivedMessageAsync` require no changes. |

## Pattern References

- `CircuitBreakerOptions` + `CircuitBreakerOptionsValidator` — inline validator pattern to follow exactly (see `src/Headless.Messaging.Core/CircuitBreaker/CircuitBreakerOptions.cs`)
- `RetryProcessorOptions` — existing sub-options class within messaging configuration
- `services.Configure<TOptions, TValidator>` — Headless.Hosting DI wiring for options with FluentValidation
- Institutional learnings: `docs/solutions/concurrency/startup-pause-gating-and-half-open-recovery.md` — cross-option invariants go in FluentValidation, not runtime compensation
