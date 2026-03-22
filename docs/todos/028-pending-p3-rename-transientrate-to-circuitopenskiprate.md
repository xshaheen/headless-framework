---
status: pending
priority: p3
issue_id: "028"
tags: ["code-review","naming"]
dependencies: []
---

# Rename transientRate to circuitOpenSkipRate

## Problem Statement

_ProcessReceivedAsync (IProcessor.NeedRetry.cs:287) names the variable transientRate and the threshold CircuitOpenRateThreshold, but what's measured is 'fraction of messages skipped because circuit was open' — not 'fraction that failed with transient exceptions'. The doc comment says 'doubles interval when >80% of executed batch are transient failures' which is incorrect.

## Findings

- **Location:** src/Headless.Messaging.Core/Processor/IProcessor.NeedRetry.cs:287
- **Discovered by:** pragmatic-dotnet-reviewer

## Proposed Solutions

_To be analyzed during triage._

## Recommended Action

Rename transientRate to circuitOpenSkipRate. Update RetryProcessorOptions.CircuitOpenRateThreshold doc comment.

## Acceptance Criteria

- [ ] Variable and doc comments accurately describe what is measured

## Notes

Source: Code review

## Work Log

### 2026-03-22 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
