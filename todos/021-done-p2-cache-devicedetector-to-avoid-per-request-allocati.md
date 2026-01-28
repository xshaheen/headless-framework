---
status: done
priority: p2
issue_id: "021"
tags: []
dependencies: []
---

# Cache DeviceDetector to avoid per-request allocations

## Problem Statement

WebHelper.GetDeviceInfo creates new DeviceDetector instance per call, which is expensive (loads/compiles hundreds of regex patterns). At 1000 RPS this causes significant memory pressure and GC pauses. File: src/Framework.Api/Extensions/Web/WebHelper.cs lines 17-26

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
- [ ] Add LRU cache keyed by user-agent hash
- [ ] Use IMemoryCache or static ConcurrentDictionary
- [ ] Consider ObjectPool<DeviceDetector>

## Notes

Source: Workflow automation

## Work Log

### 2026-01-25 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create

### 2026-01-27 - Completed

**By:** Agent
**Actions:**
- Status changed: pending → done
