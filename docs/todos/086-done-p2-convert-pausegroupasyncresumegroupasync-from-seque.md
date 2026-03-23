---
status: done
priority: p2
issue_id: "086"
tags: ["code-review","performance","messaging"]
dependencies: []
---

# Convert _PauseGroupAsync/_ResumeGroupAsync from sequential to concurrent client pause/resume

## Problem Statement

_PauseGroupAsync iterates IConsumerClient[] and awaits each PauseAsync() sequentially. At the moment of circuit trip (peak stress), all client pauses run serially — adding transport latency for each client before the next pauses. With default ConsumerThreadCount=4+, this is 4+ sequential transport calls under load. Task.WhenAll would pause all clients concurrently.

## Findings

- **Location:** src/Headless.Messaging.Core/Internal/IConsumerRegister.cs:~303,~328
- **Problem:** foreach await loop on client array — clients paused one by one
- **Impact:** Circuit trip takes N×transport_pause_latency before completing
- **Discovered by:** performance-oracle

## Proposed Solutions

### Task.WhenAll with per-client try/catch
- **Pros**: All clients pause simultaneously, fault-tolerant
- **Cons**: Slightly more complex error aggregation
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Convert both _PauseGroupAsync and _ResumeGroupAsync to use Task.WhenAll(snapshot.Select(async c => { try { await c.PauseAsync(); } catch { log; } })).

## Acceptance Criteria

- [ ] All clients in a group paused/resumed concurrently
- [ ] Exceptions from individual clients logged but do not block others
- [ ] Behavior equivalent to existing sequential version for happy path

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
