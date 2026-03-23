---
status: pending
priority: p1
issue_id: "099"
tags: ["code-review","security","log-injection"]
dependencies: []
---

# Unsanitized exception messages flow into structured log parameters

## Problem Statement

ex.Message from inner exceptions flows unsanitized into SubscriberExecutionFailedException at ISubscribeExecutor.cs:294. Raw transport name/group values are interpolated into exception strings at IConsumerRegister.cs:413. These strings propagate to structured log sinks without sanitization, enabling log injection via forged broker messages with \r\n or bidi overrides.

## Findings

- **Location 1:** src/Headless.Messaging.Core/Internal/ISubscribeExecutor.cs:294
- **Location 2:** src/Headless.Messaging.Core/Internal/IConsumerRegister.cs:413-414
- **Location 3:** src/Headless.Messaging.Core/Internal/ISubscribeExecutor.cs:230
- **Impact:** Log injection — fake log lines or hidden entries in text-based log sinks
- **Discovered by:** compound-engineering:review:security-sentinel

## Proposed Solutions

### Sanitize before interpolation
- **Pros**: Simple one-line fixes, zero design impact
- **Cons**: None
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Apply LogSanitizer.Sanitize to ex.Message, name, and group before embedding in exceptions and log calls.

## Acceptance Criteria

- [ ] ex.Message sanitized before SubscriberExecutionFailedException construction
- [ ] Raw transport name/group sanitized in 'subscriber not found' exception
- [ ] callbackEx.Message sanitized in ExecutedThresholdCallbackFailed log call

## Notes

LogSanitizer already covers groupName in the happy path but misses the exception-wrapping paths. Known pattern from docs/solutions/concurrency/circuit-breaker-transport-thread-safety-patterns.md

## Work Log

### 2026-03-23 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
