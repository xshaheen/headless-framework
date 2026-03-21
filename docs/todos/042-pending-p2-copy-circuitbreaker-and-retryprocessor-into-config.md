---
status: pending
priority: p2
issue_id: "042"
tags: ["code-review","dotnet","quality","architecture"]
dependencies: []
---

# Copy CircuitBreaker and RetryProcessor into Configure<MessagingOptions> property-copy block

## Problem Statement

Setup._RegisterCoreMessagingServices has a Configure<MessagingOptions> callback that manually copies 30+ properties from the hand-constructed options instance to the DI-registered IOptions<MessagingOptions>. CircuitBreaker and RetryProcessor are absent from this copy. Any code that resolves IOptions<MessagingOptions> and reads .CircuitBreaker or .RetryProcessor gets defaults instead of user-configured values. This is already partially fixed (IOptions<CircuitBreakerOptions> and IOptions<RetryProcessorOptions> are registered separately), but it is inconsistent and a trap for future code that reads from MessagingOptions directly.

## Findings

- **Location:** src/Headless.Messaging.Core/Setup.cs:156-188 - Configure<MessagingOptions> callback
- **Risk:** Medium - future code reading MessagingOptions.CircuitBreaker gets defaults silently
- **Discovered by:** compound-engineering:review:pragmatic-dotnet-reviewer

## Proposed Solutions

### Copy CircuitBreaker and RetryProcessor properties in the Configure callback
- **Pros**: IOptions<MessagingOptions> is consistent with what user configured
- **Cons**: Deepens the existing property-copy pattern
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Add opt.CircuitBreaker property copies and opt.RetryProcessor property copies to the Configure<MessagingOptions> callback, mirroring the other properties already there. Alternatively, expose shared mutable instances rather than copying.

## Acceptance Criteria

- [ ] IOptions<MessagingOptions>.Value.CircuitBreaker reflects user-configured values
- [ ] IOptions<MessagingOptions>.Value.RetryProcessor reflects user-configured values

## Notes

Discovered during PR #194 code review (round 2)

## Work Log

### 2026-03-21 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
