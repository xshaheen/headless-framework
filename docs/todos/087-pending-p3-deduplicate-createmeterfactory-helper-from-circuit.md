---
status: pending
priority: p3
issue_id: "087"
tags: ["code-review","testing","simplicity","messaging"]
dependencies: []
---

# Deduplicate _CreateMeterFactory() helper from circuit breaker unit test files

## Problem Statement

CircuitBreakerStateManagerTests.cs and CircuitBreakerIntegrationTests.cs both define an identical private static _CreateMeterFactory() method. This is test code duplication that will drift over time.

## Findings

- **Location:** tests/Headless.Messaging.Core.Tests.Unit/CircuitBreaker/CircuitBreakerStateManagerTests.cs:49-55, tests/Headless.Messaging.Core.Tests.Unit/CircuitBreaker/CircuitBreakerIntegrationTests.cs:35-41
- **Problem:** Identical ~7-line helper in two test files
- **Discovered by:** code-simplicity-reviewer

## Proposed Solutions

### Move to existing Harness project or a CircuitBreakerTestHelpers static class
- **Pros**: Single source of truth
- **Cons**: Minor refactor
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Extract _CreateMeterFactory() to a static CircuitBreakerTestHelpers class in the test project. Both test classes call the shared helper.

## Acceptance Criteria

- [ ] _CreateMeterFactory() defined once
- [ ] Both test files use the shared helper
- [ ] Tests still pass

## Notes

PR #194 code review finding.

## Work Log

### 2026-03-23 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
