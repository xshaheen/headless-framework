---
status: completed
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
- [x] Add OpenTelemetry metrics
- [x] Expose message count, latency, error rate
- [x] Add consumer-level metrics
- [x] Metrics exported to OTLP
- [ ] Dashboard shows key metrics (user responsibility)
- [ ] Alerts on high error rates (user responsibility)

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

### 2026-01-21 - Completed

**By:** Claude Code
**Actions:**
- Status changed: ready → completed
- Added OpenTelemetry metrics instrumentation to Framework.Messages.OpenTelemetry package
- Created MessagingMetrics class with counters and histograms for:
  - Message publish/consume counts and durations
  - Subscriber invocation counts and durations
  - Error counts by type
  - Message persistence durations
  - Message sizes
- Integrated metrics recording into DiagnosticListener for all message events
- Added MeterProviderBuilder extension method for easy metrics configuration
- Updated TracerProviderBuilder to optionally enable metrics alongside tracing
