---
status: done
priority: p1
issue_id: "001"
tags: ["code-review","dotnet","quality","architecture"]
dependencies: []
---

# Honor paused state during consumer startup

## Problem Statement

The circuit breaker can mark a group as paused while late consumer clients are still starting, but several transports still begin consumption anyway. That lets new work through after the breaker has opened, which defeats the feature's main protection during startup/restart races.

## Findings

- **Location:** src/Headless.Messaging.Core/Internal/IConsumerRegister.cs:655-666
- **Location:** src/Headless.Messaging.RabbitMq/RabbitMqConsumerClient.cs:83-95,126-153
- **Location:** src/Headless.Messaging.AzureServiceBus/AzureServiceBusConsumerClient.cs:109-124,145-161
- **Location:** src/Headless.Messaging.Nats/NatsConsumerClient.cs:88-95,305-333
- **Risk:** Open circuits can still admit fresh messages on late-starting consumers
- **Discovered by:** compound-engineering:review strict-dotnet-reviewer + pragmatic-dotnet-reviewer

## Proposed Solutions

### Gate startup on paused state
- **Pros**: Preserves circuit-breaker contract across transports and startup races
- **Cons**: Requires transport-specific changes and tests
- **Effort**: Medium
- **Risk**: Low

### Centralize pause gating in ConsumerRegister
- **Pros**: Keeps transport implementations simpler and consistent
- **Cons**: Larger refactor touching registration and listen lifecycle
- **Effort**: Large
- **Risk**: Medium


## Recommended Action

Make every transport honor an already-paused state before first subscribe/listen work starts, and add startup-race tests for RabbitMQ, Azure Service Bus, and NATS.

## Acceptance Criteria

- [ ] Consumers created after a circuit opens do not start pulling messages until resume succeeds
- [ ] RabbitMQ, Azure Service Bus, and NATS have tests covering pause-before-startup races
- [ ] PauseAsync and ResumeAsync remain idempotent after the fix

## Notes

PR #194 code review finding. The PR's brainstorm/review history already called out transport-specific pause semantics as a risk area.

## Work Log

### 2026-03-22 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin

### 2026-03-22 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready

### 2026-03-22 - Completed

**By:** Agent
**Actions:**
- Status changed: in-progress → done
