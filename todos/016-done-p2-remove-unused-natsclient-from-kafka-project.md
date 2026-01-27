---
status: done
priority: p2
issue_id: "016"
tags: []
dependencies: []
---

# Remove unused NATS.Client from Kafka project

## Problem Statement

Headless.Messaging.Kafka.csproj:7 has PackageReference to NATS.Client which is never used - likely copy-paste error inflating package.

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
- [ ] Remove <PackageReference Include="NATS.Client" /> from Kafka csproj

## Notes

Source: Workflow automation

## Work Log

### 2026-01-24 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create

### 2026-01-25 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready

### 2026-01-27 - Completed

**By:** Agent
**Actions:**
- Status changed: ready → done
