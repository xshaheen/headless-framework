---
status: done
priority: p1
issue_id: "014"
tags: ["code-review","dotnet","quality","architecture"]
dependencies: []
---

# Honor RetryProcessor settings from AddHeadlessMessaging

## Problem Statement

`AddHeadlessMessaging` validates and registers `RetryProcessorOptions`, but the DI copy of `MessagingOptions` never copies the nested `RetryProcessor` values. `MessageNeedToRetryProcessor` still reads `options.Value.RetryProcessor`, so user settings such as `AdaptivePolling = false`, a custom `MaxPollingInterval`, or a different `CircuitOpenRateThreshold` are silently ignored at runtime.

## Findings

- **Location:** src/Headless.Messaging.Core/Setup.cs:156
- **Location:** src/Headless.Messaging.Core/Setup.cs:200
- **Location:** src/Headless.Messaging.Core/Configuration/MessagingOptions.cs:186
- **Location:** src/Headless.Messaging.Core/Processor/IProcessor.NeedRetry.cs:55
- **Risk:** High - configured adaptive backpressure settings are ignored in the real runtime path
- **Discovered by:** compound-engineering:review:strict-dotnet-reviewer, code-simplicity-reviewer

## Proposed Solutions

### Inject IOptions<RetryProcessorOptions> directly
- **Pros**: Uses the already-registered options object and removes the nested copy bug
- **Cons**: Touches constructor signatures
- **Effort**: Small
- **Risk**: Low

### Copy nested RetryProcessor values into MessagingOptions
- **Pros**: Smaller surface change
- **Cons**: Keeps duplicated configuration paths
- **Effort**: Small
- **Risk**: Medium


## Recommended Action

Inject `IOptions<RetryProcessorOptions>` into `MessageNeedToRetryProcessor` and add an integration test through `AddHeadlessMessaging` so configured values are exercised through the public registration path.

## Acceptance Criteria

- [ ] Runtime retry processor reads the configured RetryProcessor values provided through AddHeadlessMessaging
- [ ] AdaptivePolling can be disabled through public configuration and the processor honors it
- [ ] MaxPollingInterval and CircuitOpenRateThreshold are honored at runtime
- [ ] An integration-style test covers the public DI registration path

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
