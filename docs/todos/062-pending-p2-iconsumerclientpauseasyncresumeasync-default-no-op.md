---
status: pending
priority: p2
issue_id: "062"
tags: ["code-review","architecture"]
dependencies: []
---

# IConsumerClient.PauseAsync/ResumeAsync default no-ops silently allow consuming while circuit thinks paused

## Problem Statement

IConsumerClient declares PauseAsync and ResumeAsync as interface default implementations that return ValueTask.CompletedTask. A transport that forgets to implement PauseAsync will compile fine — the circuit breaker will open, call PauseAsync, get CompletedTask back, and the transport will keep consuming messages while the circuit thinks it is paused. All 8 current transports implement it correctly, so the defaults only serve hypothetical future transports — a weak justification for introducing a silent failure mode.

## Findings

- **Location:** src/Headless.Messaging.Core/Transport/IConsumerClient.cs:63-75
- **Discovered by:** pragmatic-dotnet-reviewer (P3)

## Proposed Solutions

### Make PauseAsync/ResumeAsync abstract (no default implementation)
- **Pros**: Forces all transport implementors to be explicit, eliminates silent no-op
- **Cons**: Breaking change for any external IConsumerClient implementations
- **Effort**: Small
- **Risk**: Low

### Keep defaults but add [Obsolete] or XML warning doc
- **Pros**: Not breaking
- **Cons**: Does not prevent silent no-op at runtime
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Remove the default implementations. For framework-internal transports (all 8 already implement), this is non-breaking. For external consumers, make explicit no-op the intentional path with a comment.

## Acceptance Criteria

- [ ] PauseAsync and ResumeAsync have no default implementation OR throw NotImplementedException
- [ ] All 8 transport implementations still present
- [ ] Build still passes

## Notes

Source: Code review

## Work Log

### 2026-03-22 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
