---
status: ready
priority: p2
issue_id: "018"
tags: ["code-review","architecture","dotnet"]
dependencies: []
---

# SPA helper duplication (~100 LOC) across Jobs and Messaging dashboards

## Problem Statement

Five private methods are copy-pasted verbatim between Jobs Dashboard (ServiceCollectionExtensions.cs) and Messaging Dashboard (Setup.cs): _NormalizeBasePath, _CombinePathBase, _SanitizeForInlineScript, _HeadOpenRegex, _ReplaceBasePath. ~100 LOC duplicated. A fix in one silently diverges from the other.

## Findings

- **Jobs copy:** src/Headless.Jobs.Dashboard/DependencyInjection/ServiceCollectionExtensions.cs
- **Messaging copy:** src/Headless.Messaging.Dashboard/Setup.cs
- **Natural home:** src/Headless.Dashboard.Authentication/ (already shared between both)
- **Discovered by:** code-simplicity-reviewer, pragmatic-dotnet-reviewer

## Proposed Solutions

### Extract to internal static DashboardSpaHelper in Headless.Dashboard.Authentication
- **Pros**: Both packages already reference this package, zero new dependencies
- **Cons**: None
- **Effort**: Medium
- **Risk**: Low


## Recommended Action

Create internal static class DashboardSpaHelper in the shared auth package. Both dashboards call into it.

## Acceptance Criteria

- [ ] SPA injection helpers exist in exactly one location
- [ ] Both dashboards use the shared implementation
- [ ] No behavioral changes

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
