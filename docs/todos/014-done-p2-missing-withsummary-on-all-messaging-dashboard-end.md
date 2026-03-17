---
status: done
priority: p2
issue_id: "014"
tags: ["code-review","architecture","dotnet"]
dependencies: []
---

# Missing WithSummary on all messaging dashboard endpoints

## Problem Statement

Jobs dashboard has 35 WithSummary calls on endpoints. Messaging dashboard has zero. Agents and OpenAPI consumers cannot discover what messaging endpoints do, what parameters mean, or what responses look like. This directly contradicts the PR title 'align with Jobs Dashboard'.

## Findings

- **Location:** src/Headless.Messaging.Dashboard/Endpoints/MessagingDashboardEndpoints.cs (all 14 endpoint registrations)
- **Jobs pattern:** src/Headless.Jobs.Dashboard/Endpoints/DashboardEndpoints.cs (35 WithSummary calls)
- **Status values:** GET /api/published/{status} valid values (Failed,Scheduled,Succeeded,Delayed,Queued) undiscoverable
- **Discovered by:** agent-native-reviewer

## Proposed Solutions

### Add WithSummary to all 14 messaging endpoint registrations
- **Pros**: Consistent with Jobs Dashboard pattern, enables agent discoverability
- **Cons**: None
- **Effort**: Small
- **Risk**: None


## Recommended Action

Mirror the Jobs Dashboard pattern. Add WithSummary and WithDescription to all endpoint registrations.

## Acceptance Criteria

- [ ] All 14 messaging endpoints have WithSummary
- [ ] Status enum values documented in WithDescription
- [ ] Request body types documented

## Notes

Source: Code review

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
