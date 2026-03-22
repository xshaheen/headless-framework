---
status: ready
priority: p2
issue_id: "045"
tags: ["code-review","performance"]
dependencies: []
---

# Reduce GetAllStates() allocations on OTel observable gauge scrape

## Problem Statement

GetAllStates() (CircuitBreakerStateManager.cs:321-323) uses .Select().ToList() allocating a SelectIterator + List on every OTel collection cycle. With 100 groups at 10s intervals, this is significant GC pressure.

## Findings

- **Location:** src/Headless.Messaging.Core/CircuitBreaker/CircuitBreakerStateManager.cs:321-323
- **Discovered by:** performance-oracle, strict-dotnet-reviewer

## Proposed Solutions

_To be analyzed during triage._

## Recommended Action

Replace LINQ with pre-allocated List using _groupCount capacity and foreach. Or make _ObserveCircuitStates iterate _groups directly without materializing a list.

## Acceptance Criteria

- [ ] No LINQ allocation in GetAllStates()
- [ ] Observable gauge callback does not allocate a List per scrape cycle

## Notes

Source: Code review

## Work Log

### 2026-03-22 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin

### 2026-03-22 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready
