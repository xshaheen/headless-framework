---
status: done
priority: p1
issue_id: "007"
tags: ["code-review","correctness","dotnet"]
dependencies: []
---

# K8s _GetPortByName has inverted null check — port resolution never works

## Problem Statement

K8sNodeDiscoveryProvider._GetPortByName checks '!string.IsNullOrEmpty(portName)' and returns 0, which is inverted. When portName has a value (the only case needing lookup), it returns 0 immediately. The actual lookup code below is dead code. Port resolution by name is silently broken for all K8s services.

## Findings

- **Location:** src/Headless.Messaging.Dashboard.K8s/K8sNodeDiscoveryProvider.cs:190-204
- **Risk:** Critical — K8s nodes always get wrong port, agent/user sees port 0
- **Discovered by:** agent-native-reviewer

## Proposed Solutions

### Remove the ! from the condition
- **Pros**: One-character fix
- **Cons**: None
- **Effort**: Small
- **Risk**: None


## Recommended Action

Change 'if (!string.IsNullOrEmpty(portName))' to 'if (string.IsNullOrEmpty(portName))'

## Acceptance Criteria

- [ ] Port resolution by name returns correct port when portName is set
- [ ] Returns 0 only when portName is null/empty

## Notes

Single-character logic bug with significant impact on K8s deployments.

## Work Log

### 2026-03-17 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin

### 2026-03-17 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready

### 2026-03-17 - Completed

**By:** Agent
**Actions:**
- Status changed: in-progress → done
