---
status: pending
priority: p2
issue_id: "108"
tags: ["code-review","security","documentation"]
dependencies: []
---

# ForceOpenAsync/ResetAsync lack authorization guidance in XML docs

## Problem Statement

ICircuitBreakerMonitor.ForceOpenAsync and ResetAsync are operator/agent recovery methods that can halt or resume all message consumption for a group. XML docs don't warn about authorization requirements when exposing via HTTP/gRPC endpoints.

## Findings

- **Location:** src/Headless.Messaging.Core/CircuitBreaker/ICircuitBreakerMonitor.cs:120-132
- **Risk:** DoS if exposed via unauthenticated endpoint
- **Discovered by:** compound-engineering:review:security-sentinel

## Proposed Solutions

_To be analyzed during triage._

## Recommended Action

Add <remarks> to XML docs warning that HTTP/gRPC surfaces exposing these methods MUST require authorization.

## Acceptance Criteria

- [ ] XML docs include authorization warning on ForceOpenAsync
- [ ] XML docs include authorization warning on ResetAsync

## Notes

Source: Code review

## Work Log

### 2026-03-23 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
