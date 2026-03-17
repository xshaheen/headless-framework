---
status: pending
priority: p1
issue_id: "015"
tags: ["code-review","concurrency","typescript"]
dependencies: []
---

# startPolling TOCTOU race allows double polling loops

## Problem Statement

messagingStore.startPolling() checks 'pollTimer !== null' then does async work (await Promise.all) before setting pollTimer = setInterval. Between the guard check and assignment, another navigation can call startPolling, pass the null check, and start a second polling loop. Both loops write to the same reactive state causing flickering stats.

## Findings

- **Location:** src/Headless.Messaging.Dashboard/wwwroot/src/stores/messagingStore.ts:137-162
- **Risk:** High — two polling loops writing to same state at 2-second intervals
- **Reproduction:** Navigate Dashboard > Published > Dashboard fast on Slow 3G
- **Discovered by:** dan-frontend-races-reviewer

## Proposed Solutions

### Add isStarting boolean flag to close the TOCTOU window
- **Pros**: Simple, no deps, closes the race
- **Cons**: None
- **Effort**: Small
- **Risk**: None


## Recommended Action

Add 'let isStarting = false' guard that's set before the first await and cleared in finally.

## Acceptance Criteria

- [ ] Rapid navigation cannot create duplicate polling intervals
- [ ] Only one setInterval active at any time

## Notes

Also fix overlapping poll cycles (P2): setInterval fires regardless of whether previous async callback resolved. Add isPollRunning guard.

## Work Log

### 2026-03-17 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
