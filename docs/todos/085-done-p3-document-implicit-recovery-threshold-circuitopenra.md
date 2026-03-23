---
status: done
priority: p3
issue_id: "085"
tags: ["code-review","docs","messaging"]
dependencies: []
---

# Document implicit recovery threshold (CircuitOpenRateThreshold / 2.0) in RetryProcessorOptions XML docs

## Problem Statement

_AdjustPollingInterval uses `circuitOpenSkipRate <= _circuitOpenRateThreshold / 2.0` as the recovery threshold. With default CircuitOpenRateThreshold=0.8, recovery triggers at <=0.4 (40%). This hysteresis is undocumented — consumers who set a custom threshold won't know their implied recovery threshold is half the configured value.

## Findings

- **Location:** src/Headless.Messaging.Core/CircuitBreaker/RetryProcessorOptions.cs, src/Headless.Messaging.Core/Processor/IProcessor.NeedRetry.cs:320
- **Problem:** Implicit / 2.0 recovery formula undocumented
- **Discovered by:** strict-dotnet-reviewer, pragmatic-dotnet-reviewer

## Proposed Solutions

### Document in XML docs of CircuitOpenRateThreshold
- **Pros**: Simple, no code change
- **Cons**: Magic formula still in code
- **Effort**: Small
- **Risk**: Low

### Add explicit RecoveryThreshold property with cross-field validation
- **Pros**: Explicit, configurable, validated
- **Cons**: More API surface
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Add to CircuitOpenRateThreshold XML doc: 'The recovery threshold is implicitly threshold/2.0. With default 0.8, backpressure engages above 80% and recovers below 40%.'

## Acceptance Criteria

- [ ] CircuitOpenRateThreshold XML doc describes the implicit recovery formula
- [ ] Recovery threshold value documented for default (40%)
- [ ] Optionally extract to named constant RecoveryRateDivisor = 2.0

## Notes

PR #194 code review finding.

## Work Log

### 2026-03-23 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin

### 2026-03-23 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready

### 2026-03-23 - Completed

**By:** Agent
**Actions:**
- Status changed: in-progress → done
