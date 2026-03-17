---
status: ready
priority: p3
issue_id: "006"
tags: ["code-review","quality","typescript"]
dependencies: []
---

# Frontend type safety — any types, falsy-zero bug, regex escape, non-reactive computed

## Problem Statement

Multiple frontend type/correctness issues: (1) ConfirmDialog.vue uses 'any' for exception details — should be 'unknown'. (2) Published/Received use || for optional numeric params (falsy-zero bug) — should use ??. (3) Nodes.vue getCookie regex uses .replace('.') which only replaces first dot — should use /\./g. (4) DashboardLayout switchedNode computed over document.cookie is non-reactive — never updates after mount.

## Findings

- **any types:** src/.../components/common/ConfirmDialog.vue:25,30,34
- **falsy-zero:** src/.../views/Published.vue:262, Received.vue:269
- **regex escape:** src/.../views/Nodes.vue:152
- **non-reactive:** src/.../components/layout/DashboardLayout.vue:38
- **Discovered by:** shaheen-react-reviewer, dan-frontend-races-reviewer

## Proposed Solutions

### Replace any→unknown, ||→??, .replace→.replace(/\./g), computed→ref
- **Pros**: Simple targeted fixes
- **Cons**: None
- **Effort**: Small
- **Risk**: None


## Recommended Action

Apply all 4 fixes. For switchedNode, use a ref that's updated when switchToNode is called.

## Acceptance Criteria

- [ ] No any types in exception interfaces
- [ ] Numeric fallbacks use nullish coalescing
- [ ] Cookie regex escapes all dots
- [ ] Switched node display updates without page reload

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
