---
status: pending
priority: p3
issue_id: "052"
tags: ["code-review","architecture"]
dependencies: []
---

# Consider Polly v8 instead of bespoke CircuitBreakerStateManager

## Problem Statement

Polly.Core 8.6.5 and Polly.Extensions already in Directory.Packages.props. Polly v8 provides CircuitBreakerStrategyOptions, ResiliencePipelineRegistry<TKey>, callbacks (OnOpened/OnClosed/OnHalfOpened), manual Reset(), and first-class OTel integration. Current bespoke implementation is 612 lines of threading code the team owns forever.

## Findings

- **Location:** CircuitBreakerStateManager.cs (entire file)
- **Discovered by:** pragmatic-dotnet-reviewer

## Proposed Solutions

_To be analyzed during triage._

## Recommended Action

Evaluate if Polly's CircuitBreakerStrategyOptions can express the pause/resume transport callback model. If rejected, document the specific reason in CLAUDE.md.

## Acceptance Criteria

- [ ] Decision documented: use Polly or document why not
- [ ] If keeping bespoke: explicit justification in codebase

## Notes

PR summary mentions Polly v8 lazy-timestamp approach rejected because paused consumers produce no messages to trigger lazy check. Document this.

## Work Log

### 2026-03-21 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
