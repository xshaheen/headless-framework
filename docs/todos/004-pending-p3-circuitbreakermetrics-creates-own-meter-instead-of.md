---
status: pending
priority: p3
issue_id: "004"
tags: ["code-review","messaging","observability","dotnet"]
dependencies: []
---

# CircuitBreakerMetrics creates own Meter instead of using IMeterFactory

## Problem Statement

CircuitBreakerMetrics does new Meter('Headless.Messaging', '1.0.0') internally. If other code in the framework creates a Meter with the same name, two independent Meter instances exist for the same logical meter. IMeterFactory (available in .NET 8+) is the correct DI-integrated way to obtain a Meter instance, ensuring deduplication. Additionally, hardcoded '1.0.0' diverges from the actual assembly version.

## Findings

- **Location:** src/Headless.Messaging.Core/CircuitBreaker/CircuitBreakerMetrics.cs line 19
- **Discovered by:** pragmatic-dotnet-reviewer, performance-oracle, architecture-strategist

## Proposed Solutions

### Accept IMeterFactory in constructor and use meterFactory.Create('Headless.Messaging')
- **Pros**: Correct DI integration, deduplication, version from assembly
- **Cons**: None
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Inject IMeterFactory and use meterFactory.Create('Headless.Messaging') to get the shared meter. Remove IDisposable from CircuitBreakerMetrics (IMeterFactory manages lifetime).

## Acceptance Criteria

- [ ] IMeterFactory used to obtain Meter
- [ ] No duplicate meter instances

## Notes

PR #194 review.

## Work Log

### 2026-03-20 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
