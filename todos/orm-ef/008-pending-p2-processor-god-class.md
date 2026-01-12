---
status: pending
priority: p2
issue_id: "008"
tags: [code-review, architecture, dotnet, refactoring]
dependencies: []
---

# HeadlessEntityModelProcessor God Class (~650 lines)

## Problem Statement

`HeadlessEntityModelProcessor` handles 7+ distinct responsibilities in ~650 lines. This violates Single Responsibility Principle and makes the code hard to understand and maintain.

## Findings

### Location
- **File:** `src/Framework.Orm.EntityFramework/Contexts/HeadlessEntityModelProcessor.cs`
- **Lines:** 1-649

### Responsibilities Identified
1. Model creation conventions (lines 37-56)
2. DateTime value converter setup (lines 63-91)
3. Query filter configuration (lines 93-148)
4. GUID ID generation (lines 235-253)
5. Multi-tenant ID assignment (lines 220-233)
6. Create/Update/Delete/Suspend audit (lines 255-536)
7. Concurrency stamp management (lines 538-561)
8. Local message publishing (lines 567-620)

## Proposed Solutions

### Option 1: Split into focused processors (Recommended)
```
HeadlessEntityModelProcessor (orchestrator)
├── EntityAuditProcessor (create/update/delete/suspend)
├── EntityIdProcessor (GUID generation)
├── EntityTenantProcessor (multi-tenant ID)
├── QueryFilterConfigurator (query filter setup)
└── EntityMessagePublisher (event publishing)
```

**Pros:** Clear separation, easier testing, follows SRP
**Cons:** More classes to manage
**Effort:** Large
**Risk:** Medium

### Option 2: Extract only the largest concern (audit)
Move audit methods to `AuditFieldProcessor` (~300 lines).

**Pros:** Significant improvement with moderate effort
**Cons:** Still leaves some concerns mixed
**Effort:** Medium
**Risk:** Low

### Option 3: Keep as-is with better organization
Add region comments and reorganize methods logically.

**Pros:** No code changes
**Cons:** Doesn't address root issue
**Effort:** Small
**Risk:** None

## Recommended Action
<!-- To be filled during triage -->

## Technical Details

### Affected Files
- `src/Framework.Orm.EntityFramework/Contexts/HeadlessEntityModelProcessor.cs`
- Potentially new files for split classes

### Affected Components
- All HeadlessDbContext usage
- DI registration

### Database Changes Required
None

## Acceptance Criteria
- [ ] Each class has single clear responsibility
- [ ] No class exceeds ~300 lines
- [ ] All existing tests pass
- [ ] DI registration updated if needed

## Work Log
| Date | Action | Learnings |
|------|--------|-----------|
| 2026-01-12 | Identified during architecture review | 650+ line class is a code smell |

## Resources
- Single Responsibility Principle
