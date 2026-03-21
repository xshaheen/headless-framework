---
status: pending
priority: p3
issue_id: "070"
tags: ["code-review","quality"]
dependencies: []
---

# docs/llms/messaging.txt shows wrong type for MaxPollingInterval

## Problem Statement

LLM-facing documentation shows: options.RetryProcessor.MaxPollingInterval = 900; // seconds. Actual property type is TimeSpan (default: TimeSpan.FromMinutes(15)). AI agents generating config from docs will produce type errors.

## Findings

- **Location:** docs/llms/messaging.txt:99
- **Discovered by:** agent-native-reviewer

## Proposed Solutions

_To be analyzed during triage._

## Recommended Action

Update to: options.RetryProcessor.MaxPollingInterval = TimeSpan.FromMinutes(15);

## Acceptance Criteria

- [ ] LLM docs match actual property types

## Notes

Source: Code review

## Work Log

### 2026-03-21 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
