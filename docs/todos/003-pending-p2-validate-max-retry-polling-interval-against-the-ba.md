---
status: pending
priority: p2
issue_id: "003"
tags: ["code-review","dotnet","quality"]
dependencies: []
---

# Validate max retry polling interval against the base retry interval

## Problem Statement

RetryProcessorOptionsValidator accepts MaxPollingInterval values that are lower than MessagingOptions.FailedRetryInterval. With that configuration, the adaptive backoff path can reduce the current interval below the baseline, so a 'max' interval ends up making retry polling more aggressive during outages instead of less.

## Findings

- **Location:** src/Headless.Messaging.Core/CircuitBreaker/RetryProcessorOptionsValidator.cs:11-14
- **Location:** src/Headless.Messaging.Core/Processor/IProcessor.NeedRetry.cs:289-296
- **Risk:** Misconfiguration can turn backpressure into extra retry pressure
- **Discovered by:** compound-engineering:review strict-dotnet-reviewer

## Proposed Solutions

### Cross-validate against FailedRetryInterval at registration time
- **Pros**: Prevents invalid configurations before runtime
- **Cons**: Requires validator access to the base messaging options
- **Effort**: Small
- **Risk**: Low

### Clamp MaxPollingInterval to the base interval in the processor
- **Pros**: Makes runtime behavior safe even with bad configuration
- **Cons**: Silently rewrites invalid input unless also logged/validated
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Reject or clamp MaxPollingInterval values below FailedRetryInterval so adaptive backoff never reduces the effective interval below the configured baseline.

## Acceptance Criteria

- [ ] Configuration with MaxPollingInterval below FailedRetryInterval fails validation or is safely clamped
- [ ] Adaptive backoff cannot lower the effective polling interval below the base retry interval
- [ ] Tests cover the invalid configuration path

## Notes

PR #194 code review finding. The current validator only checks > 0 and <= 24h.

## Work Log

### 2026-03-22 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
