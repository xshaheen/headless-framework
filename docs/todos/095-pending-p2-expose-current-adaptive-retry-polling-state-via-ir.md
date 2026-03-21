---
status: pending
priority: p2
issue_id: "095"
tags: ["code-review","dotnet","messaging"]
dependencies: []
---

# Expose current adaptive retry polling state via IRetryProcessorMonitor

## Problem Statement

RetryProcessorOptions exposes configuration (MaxPollingInterval, CircuitOpenRateThreshold) but there is no public API to observe the current effective polling interval at runtime as it adapts. An agent cannot determine whether the system is in a degraded backed-off state vs normal polling. It cannot decide whether to take remediation actions based on retry processor health.

## Findings

- **Location:** src/Headless.Messaging.Core/CircuitBreaker/RetryProcessorOptions.cs (absent runtime state), src/Headless.Messaging.Core/Processor/IProcessor.NeedRetry.cs
- **Risk:** Agent-native observability gap — retry processor degradation invisible to agents
- **Discovered by:** agent-native-reviewer

## Proposed Solutions

### Add IRetryProcessorMonitor interface with TimeSpan CurrentPollingInterval and bool IsBackedOff
- **Pros**: Clean interface, injectable via DI
- **Cons**: New public interface adds API surface
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Add IRetryProcessorMonitor interface exposing TimeSpan CurrentPollingInterval and bool IsBackedOff. Register in DI. The retry processor already tracks _currentIntervalTicks internally.

## Acceptance Criteria

- [ ] IRetryProcessorMonitor interface added with CurrentPollingInterval and IsBackedOff
- [ ] Registered as singleton in DI
- [ ] README updated with usage example

## Notes

PR #194.

## Work Log

### 2026-03-21 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
