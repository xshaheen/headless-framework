---
status: ready
priority: p2
issue_id: "045"
tags: []
dependencies: []
---

# observability-missing-metrics

## Problem Statement

No built-in metrics for message processing (throughput, latency, errors). Makes production monitoring difficult.

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
- [ ] Add OpenTelemetry metrics
- [ ] Expose message count, latency, error rate
- [ ] Add consumer-level metrics
- [ ] Metrics exported to OTLP
- [ ] Dashboard shows key metrics
- [ ] Alerts on high error rates

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
