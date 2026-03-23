---
status: ready
priority: p3
issue_id: "103"
tags: ["code-review","dotnet","api-design"]
dependencies: []
---

# GetAllStates returns IReadOnlyList<KeyValuePair<...>> — awkward .NET API

## Problem Statement

GetAllStates returns IReadOnlyList<KeyValuePair<string, CircuitBreakerState>>. In .NET, this would be more idiomatically expressed as IReadOnlyDictionary<string, CircuitBreakerState> or a dedicated snapshot type.

## Findings

- **Location:** src/Headless.Messaging.Core/CircuitBreaker/ICircuitBreakerMonitor.cs
- **Discovered by:** compound-engineering:review:pragmatic-dotnet-reviewer

## Proposed Solutions

_To be analyzed during triage._

## Recommended Action

Return IReadOnlyDictionary<string, CircuitBreakerState> or consider adding GetAllSnapshots() for richer diagnostic queries.

## Acceptance Criteria

- [ ] Return type is more idiomatic

## Notes

Agent-native reviewer also flagged need for GetAllSnapshots bulk method.

## Work Log

### 2026-03-23 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin

### 2026-03-23 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready
