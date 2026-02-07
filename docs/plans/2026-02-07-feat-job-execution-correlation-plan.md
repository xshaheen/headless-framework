# feat: Job Execution Correlation Propagation

> **Scope:** Automatic correlation chain from scheduled job execution → messages published by job handlers. MassTransit-style parent ID propagation via ambient context.
>
> **Not in scope:** Regular consumer→publish correlation (Full Merge US-030), OpenTelemetry Activity integration (Full Merge US-032–034), dashboard tracing UI.

## Overview

When a scheduled job handler publishes messages via `IOutboxPublisher` or `IDirectPublisher`, the published messages should automatically carry the job execution's correlation ID — without the handler having to pass explicit headers.

**Current state:**
- `ScheduledJobDispatcher` sets `CorrelationId = null` and empty headers on `ConsumeContext<ScheduledTrigger>`
- Publishers default `CorrelationId` to `MessageId` when not provided
- No ambient context bridge exists between dispatcher and publishers
- Result: job→message correlation chain is broken

**After this change:**
- `ScheduledJobDispatcher` sets `CorrelationId = executionId` and populates headers with job metadata
- New `MessagingCorrelationScope` (AsyncLocal) set by dispatcher before invoking handler
- Publishers auto-read from ambient scope as fallback, inheriting the job execution's correlation
- Handler code unchanged — correlation propagation is automatic

---

## Approach Comparison: Ticker vs MassTransit vs Chosen

### MassTransit Model

MassTransit propagates correlation via **three distinct IDs** and **scoped DI**:

| Header | Meaning | Propagation |
|--------|---------|-------------|
| `ConversationId` | Identifies the entire message tree | Copied unchanged from consumed message to all published messages |
| `InitiatorId` | The message that caused this one | Set to consumed message's `MessageId` |
| `CorrelationId` | Business-level correlation | Per-message, user-controlled (from saga state, etc.) |

**Mechanism:** `ScopedConsumeContextProvider` registered as scoped service → `ConsumeSendPipeAdapter` in the send pipeline reads the scoped `ConsumeContext` and copies headers. Works because MassTransit resolves publishers from the same DI scope as the consumer.

**Why not for us:** Headless.Messaging publishers (`IOutboxPublisher`, `IDirectPublisher`) are injected by the consumer — not resolved from the dispatcher's DI scope. Scoped DI would require publishers to be scoped services resolved from the dispatcher scope, which breaks the current architecture where publishers are injected into consumers via constructor DI.

### Ticker Model

Ticker propagated correlation via **ParentId on entities** and a **static ConcurrentDictionary index**:

- `ParentId` property on `TickerJobContext`, `TickerJobExecution`, `TickerScheduledJob`
- `TickerCancellationTokenManager._ParentIdIndex` (ConcurrentDictionary) for O(1) sibling lookup
- OpenTelemetry `Activity` with `Headless.Ticker.job.parent_id` tags
- `RunCondition` enum for conditional child execution (if parent succeeds/fails)

**Why not for us:** Ticker's model is about **job→job** parent-child execution trees. We don't need execution trees — the unified messaging system provides better composition via message publishing from handlers. The `_ParentIdIndex` and `RunCondition` are over-engineered for simple correlation propagation.

### Chosen Approach: AsyncLocal Ambient Context

**Best of both worlds:** MassTransit's "automatic propagation without handler changes" + framework's existing AsyncLocal pattern.

| Decision | Choice | Rationale |
|----------|--------|-----------|
| **Propagation mechanism** | `AsyncLocal` (not scoped DI) | Matches existing `OutboxTransactionHolder` pattern (`OutboxPublisher.cs:31`). Publishers aren't resolved from dispatcher scope. |
| **Correlation model** | Single `CorrelationId` + `Sequence` (not ConversationId/InitiatorId split) | Simpler. Headless.Messaging already has `headless-corr-id` + `headless-corr-seq` headers. No need for 3 IDs when we're tracing job→messages, not multi-hop sagas. |
| **Override behavior** | Explicit headers win over ambient (like MassTransit) | `ConsumeSendPipeAdapter` runs before user pipe in MassTransit — explicit user headers override. Same principle here. |
| **Thread safety** | `Interlocked.Increment` for sequence | Multiple messages published from one handler get incrementing sequences (1, 2, 3...) under same correlation. |
| **Nesting** | Parent scope restore on `Dispose()` | If a handler begins a nested scope (unlikely but safe), outer scope restores. Same as MassTransit's scoped behavior. |

**Why AsyncLocal over scoped DI for this codebase:**

1. **Architecture fit** — `OutboxPublisher` already uses `AsyncLocal<OutboxTransactionHolder>` (line 31). Adding another AsyncLocal is consistent; converting to scoped DI would require restructuring publisher registration.
2. **Cross-scope reach** — The `ScheduledJobDispatcher` creates a DI scope (`scopeFactory.CreateAsyncScope()` at line 21), but the handler's publishers come from the handler's own DI resolution chain. AsyncLocal crosses scope boundaries naturally.
3. **Simplicity** — No DI registration changes, no new scoped services, no pipeline adapters. Just one sealed class with `Begin()`/`Dispose()`.

## Technical Approach

```
ScheduledJobDispatcher.DispatchAsync(job, execution, ct):
  1. Build ConsumeContext with CorrelationId = execution.Id  (not null)
  2. Populate headers: headless-corr-id, headless-msg-id, job metadata
  3. using (MessagingCorrelationScope.Begin(execution.Id, sequence: 0))
  4.   handler.Consume(context, ct)
  5.     handler calls outbox.PublishAsync(topic, msg, ct)  // no explicit headers
  6.       OutboxPublisher._PublishInternalAsync():
  7.         if no CorrelationId in headers:
  8.           check MessagingCorrelationScope.Current  // <-- NEW
  9.           if scope exists: use scope.CorrelationId, increment sequence
  10.          else: default to MessageId (existing behavior)
```

### MessagingCorrelationScope Design

```csharp
// In Headless.Messaging.Abstractions
public sealed class MessagingCorrelationScope : IDisposable
{
    private static readonly AsyncLocal<MessagingCorrelationScope?> _current = new();

    public static MessagingCorrelationScope? Current => _current.Value;

    public string CorrelationId { get; }

    private int _sequence;

    private readonly MessagingCorrelationScope? _parent;

    private MessagingCorrelationScope(string correlationId, int sequence)
    {
        CorrelationId = correlationId;
        _sequence = sequence;
        _parent = _current.Value;
        _current.Value = this;
    }

    public static MessagingCorrelationScope Begin(string correlationId, int sequence = 0)
        => new(correlationId, sequence);

    /// <summary>
    /// Atomically increments and returns the next sequence number.
    /// Each published message within this scope gets an incrementing sequence.
    /// </summary>
    public int IncrementSequence() => Interlocked.Increment(ref _sequence);

    public void Dispose() => _current.Value = _parent;
}
```

**Key design decisions:**

- `_sequence` is a private `int` field (not property) so `Interlocked.Increment` works via `ref`. The previous plan had `public int Sequence { get; private set; }` which doesn't work with `Interlocked.Increment` on a property backing field in the obvious way.
- Sequence starts at 0, first `IncrementSequence()` returns 1. This means the dispatcher's own context message (sequence 0) and handler-published messages (1, 2, 3...) have distinct sequences.
- Nesting-safe via `_parent` restore — same pattern as MassTransit's scope nesting, though MassTransit achieves it via DI scope hierarchy.

### Publisher Integration Point

Both `OutboxPublisher._PublishInternalAsync` (line 191) and `DirectPublisher._GenerateHeaders` (line 188) have the same pattern:

```csharp
// Current code (OutboxPublisher:191-195):
if (!headers.ContainsKey(Headers.CorrelationId))
{
    headers.Add(Headers.CorrelationId, value1);  // value1 = messageId
    headers.Add(Headers.CorrelationSequence, 0.ToString());
}

// After change:
if (!headers.ContainsKey(Headers.CorrelationId))
{
    var scope = MessagingCorrelationScope.Current;
    if (scope is not null)
    {
        headers.Add(Headers.CorrelationId, scope.CorrelationId);
        headers.Add(Headers.CorrelationSequence, scope.IncrementSequence().ToString(CultureInfo.InvariantCulture));
    }
    else
    {
        headers.Add(Headers.CorrelationId, value1);  // value1 = messageId (existing behavior)
        headers.Add(Headers.CorrelationSequence, "0");
    }
}
```

**Explicit headers always win** — if the caller passes `Headers.CorrelationId` in the dictionary, the `ContainsKey` check returns true and the ambient scope is never consulted. Same behavior as MassTransit where user send filters run after `ConsumeSendPipeAdapter`.

---

## Stories

### US-006: Create MessagingCorrelationScope [S]

AsyncLocal-based ambient correlation context.

**Files to Study:**
- `src/Headless.Messaging.Core/Internal/OutboxPublisher.cs:31` — existing AsyncLocal pattern
- `src/Headless.Messaging.Core/Transactions/OutboxTransaction.cs:13` — holder pattern
- `src/Headless.Messaging.Abstractions/Messages/Headers.cs` — header constants

**Acceptance Criteria:**
- [ ] `MessagingCorrelationScope` sealed class in `Headless.Messaging` namespace (Abstractions package)
- [ ] `AsyncLocal<MessagingCorrelationScope?>` for ambient context with `static Current` property
- [ ] `Begin()` factory, `IDisposable` for cleanup, nesting-safe via parent scope restore
- [ ] `CorrelationId` (string) and `_sequence` (int field) with `Interlocked.Increment`
- [ ] XML docs

### US-007: Populate correlation in ScheduledJobDispatcher [S]

Set real correlation ID and ambient scope when dispatching scheduled jobs.

**Files to Study:**
- `src/Headless.Messaging.Core/Scheduling/ScheduledJobDispatcher.cs:27-43` — current context creation
- `src/Headless.Messaging.Abstractions/Messages/Headers.cs` — header keys

**Acceptance Criteria:**
- [ ] `CorrelationId = execution.Id.ToString()` instead of `null`
- [ ] Headers populated: `headless-corr-id` = execution ID, `headless-corr-seq` = "0"
- [ ] `using MessagingCorrelationScope.Begin(execution.Id.ToString())` wrapping handler invocation
- [ ] Scope disposed in `finally` block even on handler failure
- [ ] Existing `IConsumerLifecycle` hooks still fire within scope

### US-008: Make publishers read MessagingCorrelationScope [M]

OutboxPublisher and DirectPublisher auto-inherit correlation from ambient scope.

**Files to Study:**
- `src/Headless.Messaging.Core/Internal/OutboxPublisher.cs:191-195` — `_PublishInternalAsync` correlation fallback
- `src/Headless.Messaging.Core/Internal/DirectPublisher.cs:188-192` — `_GenerateHeaders` correlation fallback

**Acceptance Criteria:**
- [ ] Both publishers: when `CorrelationId` not in explicit headers, check `MessagingCorrelationScope.Current`
- [ ] If scope exists: use `scope.CorrelationId`, set `CorrelationSequence = scope.IncrementSequence()`
- [ ] If scope is null: existing behavior (default CorrelationId = MessageId, sequence = 0)
- [ ] Explicit headers always override ambient scope
- [ ] No behavior change when no scope active (backward compatible)

### US-009: Unit tests [M]

**Files to Study:**
- `tests/Headless.Messaging.Core.Tests.Unit/`
- `tests/Headless.Messaging.Abstractions.Tests.Unit/`

**Acceptance Criteria:**
- [ ] `MessagingCorrelationScope`: begin/dispose lifecycle, nesting, thread-safe sequence increment
- [ ] `ScheduledJobDispatcher`: sets CorrelationId = executionId, populates headers, begins scope
- [ ] `OutboxPublisher`: inherits correlation from scope when no explicit headers
- [ ] `DirectPublisher`: same behavior
- [ ] Explicit headers override ambient scope
- [ ] No scope active: existing behavior preserved (CorrelationId = MessageId)
- [ ] `TestBase`, `AbortToken`, `should_*_when_*` naming

### US-010: Update README [XS]

**Files to Study:**
- `src/Headless.Messaging.Core/README.md`

**Acceptance Criteria:**
- [ ] Document automatic job→message correlation propagation
- [ ] Example: scheduled job handler publishing a message, showing correlation flows automatically

---

## Unresolved Questions

None — all design decisions resolved via research:

1. ~~AsyncLocal vs scoped DI~~ → AsyncLocal (matches existing pattern, crosses DI scope boundaries)
2. ~~Single CorrelationId vs ConversationId/InitiatorId split~~ → Single CorrelationId + Sequence (simpler, matches existing headers)
3. ~~Sequence numbering~~ → Starts at 0 (dispatcher context), `IncrementSequence()` returns 1+ for handler-published messages

---

## References

### Internal
- `src/Headless.Messaging.Core/Internal/OutboxPublisher.cs:31` — existing `AsyncLocal<OutboxTransactionHolder>` pattern
- `src/Headless.Messaging.Core/Internal/OutboxPublisher.cs:191-195` — correlation header generation
- `src/Headless.Messaging.Core/Internal/DirectPublisher.cs:188-192` — correlation header generation
- `src/Headless.Messaging.Core/Scheduling/ScheduledJobDispatcher.cs:21-43` — DI scope creation + context creation
- `src/Headless.Messaging.Core/Internal/ISubscribeInvoker.cs:254-270` — callback correlation propagation (future: extend to all consumers)
- `src/Headless.Messaging.Abstractions/Messages/Headers.cs` — header constants
- `docs/merge/TICKER-MESSAGING-INTEGRATION-QUICKREF.md:259` — "Add CorrelationId to both job and message"

### External (Research Sources)
- **MassTransit correlation model:** ConversationId/InitiatorId/CorrelationId headers. `ScopedConsumeContextProvider` + `ConsumeSendPipeAdapter` for ambient propagation. User send filters override ambient context.
- **Ticker correlation model:** `ParentId` on entities, `TickerCancellationTokenManager._ParentIdIndex` for O(1) sibling lookup, OpenTelemetry `Activity` with parent_id tags, `RunCondition` for conditional child execution.
