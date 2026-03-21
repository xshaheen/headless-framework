---
status: done
priority: p2
issue_id: "016"
tags: ["code-review","dotnet","architecture","quality"]
dependencies: []
---

# Make WithCircuitBreaker registration follow final consumer metadata

## Problem Statement

`WithCircuitBreaker(...)` snapshots the current consumer metadata and registers the override immediately. If callers invoke `Group(...)` or other metadata-affecting methods afterward, the circuit-breaker override stays attached to the old group instead of the final one. That makes the fluent API order-dependent and can silently apply overrides to the wrong consumer group.

## Findings

- **Location:** src/Headless.Messaging.Core/ConsumerBuilder.cs:87
- **Location:** src/Headless.Messaging.Core/ServiceCollectionConsumerBuilder.cs:79
- **Risk:** Medium - per-consumer overrides can be registered against stale metadata depending on fluent call order
- **Discovered by:** compound-engineering:review:strict-dotnet-reviewer, pragmatic-dotnet-reviewer

## Proposed Solutions

### Store pending circuit-breaker config on the builder and register it when metadata is finalized
- **Pros**: Eliminates order dependence and keeps fluent API intuitive
- **Cons**: Requires builder state changes
- **Effort**: Medium
- **Risk**: Low

### Update registry entries whenever metadata-affecting methods change the group
- **Pros**: Smaller localized fix
- **Cons**: More bookkeeping and easier to miss future metadata changes
- **Effort**: Medium
- **Risk**: Medium


## Recommended Action

Treat the breaker override as pending builder state and register it only from final resolved metadata. Add tests for both call orders.

## Acceptance Criteria

- [ ] Calling WithCircuitBreaker before or after Group produces the same effective override
- [ ] The IServiceCollection builder path and the direct MessagingOptions builder path behave consistently
- [ ] Tests cover multiple fluent call orders

## Notes

Discovered during PR #194 code review

## Work Log

### 2026-03-21 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin

### 2026-03-21 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready

### 2026-03-21 - Completed

**By:** Agent
**Actions:**
- Status changed: in-progress → done
