---
status: pending
priority: p3
issue_id: "008"
tags: ["code-review","testing","quality"]
dependencies: []
---

# Include NATS integration tests in the solution

## Problem Statement

The new `Headless.Messaging.Nats.Tests.Integration` project is committed on the branch, but it is not referenced by `headless-framework.slnx`. The standard solution build/test workflow will therefore miss the new integration coverage.

## Findings

- **Location:** headless-framework.slnx:200-222; tests/Headless.Messaging.Nats.Tests.Integration/Headless.Messaging.Nats.Tests.Integration.csproj
- **Impact:** Normal solution-level validation does not discover or run the new NATS integration tests
- **Discovered by:** code review

## Proposed Solutions

### Add the project under /Messaging/Tests/ in the solution
- **Pros**: Smallest fix
- **Cons**: None significant
- **Effort**: Small
- **Risk**: Low

### Document a separate CI/test invocation
- **Pros**: Works if solution inclusion is intentionally omitted
- **Cons**: Easier to drift or forget
- **Effort**: Small
- **Risk**: Medium


## Recommended Action

Add the integration test project to `headless-framework.slnx` so it participates in the standard validation path.

## Acceptance Criteria

- [ ] The NATS integration test project is referenced from `headless-framework.slnx`
- [ ] Solution-level test discovery includes the new project
- [ ] CI or documented local test workflow covers the project

## Notes

Review of branch xshaheen/review-transports on 2026-03-25.

## Work Log

### 2026-03-25 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
