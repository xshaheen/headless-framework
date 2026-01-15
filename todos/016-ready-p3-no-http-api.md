---
status: ready
priority: p3
issue_id: "016"
tags: [code-review, agent-native, api, permissions]
dependencies: []
---

# No HTTP API Endpoints for Permission Management

## Problem Statement

The permission system exposes clean programmatic interfaces but no REST API endpoints. External agents, webhooks, and cross-service calls cannot directly query or modify permissions without custom controllers.

## Findings

**Available programmatic APIs:**
- `IPermissionManager` - Check, grant, revoke permissions
- `IPermissionDefinitionManager` - List permissions and groups
- Extension methods for convenience

**Missing:**
- No HTTP controllers/endpoints
- No standardized REST contract

## Proposed Solutions

### Option A: Add Framework.Permissions.Api Package (Recommended)
**Pros:** Standardized REST API, reusable across apps
**Cons:** Additional package
**Effort:** Medium
**Risk:** Low

Suggested endpoints:
```
GET  /api/permissions                    # List all defined permissions
GET  /api/permissions/{name}             # Get single permission definition
GET  /api/permissions/check?names=...    # Check permissions for current user
POST /api/permissions/grants             # Grant permission
DELETE /api/permissions/grants           # Revoke permission
```

### Option B: Document Controller Implementation
**Pros:** No framework change
**Cons:** Each app implements differently
**Effort:** Small
**Risk:** Low

## Recommended Action

<!-- Fill during triage -->

## Technical Details

**New Package:** `Framework.Permissions.Api`

## Acceptance Criteria

- [ ] REST endpoints for permission operations
- [ ] OpenAPI documentation generated
- [ ] Authorization on admin endpoints

## Work Log

| Date | Action | Learnings |
|------|--------|-----------|
| 2026-01-13 | Created from code review | Agent-Native review finding |

## Resources

- Agent-Native Reviewer findings

### 2026-01-15 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready
