---
status: ready
priority: p2
issue_id: "039"
tags: []
dependencies: []
---

# n-plus-1-assembly-scanning

## Problem Statement

ScanConsumers performs multiple GetInterfaces() calls per type instead of one call per assembly. Causes O(n²) behavior slowing startup.

## Findings

- **Status:** Identified during workflow execution
- **Priority:** p2

## Proposed Solutions

### Option 1: [Primary solution]
- **Pros**: [Benefits]
- **Cons**: [Drawbacks]
- **Effort**: Small/Medium/Large
- **Risk**: Low/Medium/High

## Recommended Action

[To be filled during triage]

## Acceptance Criteria
- [ ] Cache GetInterfaces results
- [ ] Use single LINQ query per assembly
- [ ] Benchmark shows <100ms for large assemblies
- [ ] Startup time improved
- [ ] No N+1 queries in reflection

## Notes

Source: Workflow automation

## Work Log

### 2026-01-20 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create

### 2026-01-20 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready
