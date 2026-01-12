---
status: pending
priority: p3
issue_id: "119"
tags: [code-review, dotnet, serilog, code-cleanup]
dependencies: []
---

# Redundant OutputTemplate Constant in ApiSerilogFactory

## Problem Statement

`ApiSerilogFactory.OutputTemplate` simply references `SerilogFactory.OutputTemplate` without adding value. Users can reference `SerilogFactory.OutputTemplate` directly.

## Findings

**Source:** strict-dotnet-reviewer, code-simplicity-reviewer, pattern-recognition-specialist agents

**Affected Files:**
- `src/Framework.Api.Logging.Serilog/ApiSerilogFactory.cs:15`

**Current Code:**
```csharp
public const string OutputTemplate = SerilogFactory.OutputTemplate;
```

## Proposed Solutions

### Option 1: Remove Redundant Constant (Recommended)
**Pros:** Less indirection, cleaner API surface
**Cons:** Breaking change if anyone uses ApiSerilogFactory.OutputTemplate
**Effort:** Trivial
**Risk:** Low (unlikely to be used externally)

### Option 2: Keep and Document
**Pros:** API convenience for users in API layer
**Cons:** Unnecessary indirection
**Effort:** None
**Risk:** None

## Technical Details

**Affected Components:** ApiSerilogFactory
**Files to Modify:** 1 file

## Acceptance Criteria

- [ ] Redundant constant removed OR documented why it exists
- [ ] Code compiles without errors

## Work Log

| Date | Action | Learnings |
|------|--------|-----------|
| 2026-01-11 | Created from code review | Avoid unnecessary indirection |

## Resources

- None
