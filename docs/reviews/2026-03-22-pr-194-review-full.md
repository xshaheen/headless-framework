---
pr: 194
branch: worktree-xshaheen/messaging-circuit-breaker-and-retry-backpressure
reviewers:
  - strict-dotnet-reviewer
  - pragmatic-dotnet-reviewer
  - code-simplicity-reviewer
  - security-sentinel
  - performance-oracle
  - agent-native-reviewer
  - learnings-researcher
findings:
  p1_critical: 6
  p2_important: 12
  p3_nice_to_have: 10
  total: 28
timestamp: 2026-03-22T00:00:00Z
---

# Code Review Summary — PR #194

**feat(messaging): per-group circuit breaker and adaptive retry backpressure**

## Reviewers Used

- **strict-dotnet-reviewer** — async correctness, threading, IDisposable, API design (Stephen Toub level)
- **pragmatic-dotnet-reviewer** — real-world operational readiness, DX, "should you?" questions (Scott Hanselman perspective)
- **code-simplicity-reviewer** — YAGNI, duplication, unnecessary abstractions
- **security-sentinel** — log injection, DoS, resource exhaustion, TOCTOU
- **performance-oracle** — allocations, lock contention, OTel cardinality, hot paths
- **agent-native-reviewer** — programmatic control parity, HTTP surface, agent discoverability
- **learnings-researcher** — prior solution docs from `docs/solutions/concurrency/`

## Key Findings

### P1 Critical (6) — Blocks Merge

1. **Resume callback races Dispose** — Task.Run fire-and-forget in `_OnOpenTimerElapsed` can invoke callback after Dispose
2. **ResetBackpressureAsync torn writes** — plain writes to counter fields from any thread while ProcessAsync reads
3. **ConsumerTasks concurrent writes** — `List<Task>` is not thread-safe for concurrent `Add` from LongRunning tasks
4. **ValueTask.DisposeAsync() discarded inside lock** — `AddClientAsync` fires and forgets real async disposal (Pulsar, ASB)
5. **_pauseGate/_paused race across all transports** — window between CAS and TCS assignment can permanently block consumer
6. **Double escalation-increment on pause failure** — `_ReopenAfterResumeFailureAsync` bumps escalation twice (4x instead of 2x)

### P2 Important (12) — Should Fix

1. **Log injection: 7 unsanitized log sites + duplicate _SanitizeGroupName** — security + 55 LOC duplication
2. **_knownGroups missing Volatile.Write** — ARM64 visibility risk
3. **GetAllStates() empty during startup** — OTel gauge gap before RegisterKnownGroups
4. **ConsumerCircuitBreakerOptions setters should be init** — prevents post-registration mutation
5. **No upper bound on SuccessfulCyclesToResetEscalation** — can permanently prevent escalation reset
6. **Fire-and-forget tasks swallow exceptions in retry processor** — silent failures
7. **_disposed reset in StartAsync defeats idempotency** — allows double-dispose race
8. **512 vs 256 limit mismatch** — ResetAsync allows 512 but sanitizer truncates to 256
9. **ValueTuple boxing on Timer creation** — avoidable allocation per circuit trip
10. **GetAllStates() LINQ allocations** — .Select().ToList() every OTel scrape
11. **Per-message IsOpen() lookups in retry loop** — should cache per group for batch
12. **OTel gauge bypasses _SafeTag** — cardinality guard inconsistency

### P3 Nice-to-Have (10)

- Simplify `_GetOpenDuration` with `Math.Pow`
- Clarify `IsOpen` vs `GetState` semantics
- Rename `transientRate` to `circuitOpenSkipRate`
- Add `TripAsync` API + dashboard HTTP endpoints
- Consider merging `ICircuitBreakerMonitor`/`ICircuitBreakerStateManager`
- Eliminate `ConsumerCircuitBreakerRegistration` (~90 LOC)
- Extract `ConsumerPauseGate` to reduce 7-transport duplication
- Add "why not Polly" rationale to class doc
- Replace `ArgumentOutOfRangeException` with `Headless.Checks` guards
- Add `IAsyncDisposable` to `CircuitBreakerStateManager`

## Prior Art (Learnings Researcher)

Two solution docs directly applicable:
- `docs/solutions/concurrency/circuit-breaker-transport-thread-safety-patterns.md` — 8 patterns from prior review
- `docs/solutions/concurrency/startup-pause-gating-and-half-open-recovery.md` — 3 lifecycle gaps

## References

- Todos: `docs/todos/*-pending-*.md`
- PR: https://github.com/xshaheen/headless-framework/pull/194
