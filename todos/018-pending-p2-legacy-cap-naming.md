---
status: pending
priority: p2
issue_id: "018"
tags: [naming, terminology, cap-rename, legacy]
dependencies: []
---

# Legacy CAP Naming Remains (58 Occurrences)

## Problem Statement

Despite comprehensive CAP→Messaging rename, 58 legacy references remain in diagnostics, comments, and error messages.

## Findings

**Remaining Locations**:
- Diagnostic listener names: "Headless.Messaging." prefix (should be "Framework.Messages.")
- GitHub issue references: "see: https://github.com/dotnetcore/CAP/issues/63"
- Internal class names: `ICapPublisher`, `CapOptions`
- XML comments: "CAP" references

**Examples**:
```csharp
// MessageDiagnosticListenerNames.cs:10
private const string _Prefix = "Headless.Messaging."; // Inconsistent

// ISubscribeExector.Default.cs:64
var error = $"... see: https://github.com/dotnetcore/CAP/issues/63"; // External reference
```

## Proposed Solutions

### Option 1: Complete Rename (RECOMMENDED)
**Effort**: 2-3 hours

Replace all occurrences:
- `Headless.Messaging.` → `Framework.Messages.`
- CAP GitHub links → internal documentation
- `ICapPublisher` → `IOutboxPublisher` (already exists)
- Comments: CAP → Messaging

### Option 2: Keep Internal Names
**Effort**: 1 hour
**Rationale**: Non-breaking if internal-only

Only rename public-facing strings.

## Recommended Action

Implement Option 1 for consistency.

## Acceptance Criteria

- [ ] Zero occurrences of "CAP" in public APIs
- [ ] Diagnostic names use Framework.Messages prefix
- [ ] External URLs point to headless-framework docs
- [ ] Internal names consistent with terminology
- [ ] Breaking changes documented (if any)

## Work Log

### 2026-01-20 - Issue Created

**By:** Claude Code (Pattern Recognition Specialist Agent)

**Actions:**
- Searched codebase for remaining CAP references
- Categorized by visibility and impact
- Recommended complete rename
