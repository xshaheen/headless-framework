---
status: ready
priority: p3
issue_id: "019"
tags: [code-cleanup, dead-code, technical-debt]
dependencies: []
---

# Dead Code: ObjectMethodExecutor Folder (~800 LOC)

## Problem Statement

`src/Framework.Messages.Core/Internal/ObjectMethodExecutor/` folder contains ~800 lines of unused reflection utilities from .NET Core MVC, leftover from attribute-based consumer pattern.

## Findings

**Folder Contents**:
- CoercedAwaitableInfo.cs
- ObjectMethodExecutor.cs
- ObjectMethodExecutorAwaitable.cs
- ObjectMethodExecutorFSharpSupport.cs

**Status**: Not referenced anywhere in codebase (verified with `rg "ObjectMethodExecutor"`).

**Origin**: Copied from ASP.NET Core MVC for attribute-based routing (now replaced by IConsume<T>).

## Proposed Solutions

### Option 1: Delete Immediately (RECOMMENDED)
**Effort**: 5 minutes
**Risk**: None (unused)

```bash
rm -rf src/Framework.Messages.Core/Internal/ObjectMethodExecutor/
```

## Recommended Action

Delete folder - simple cleanup, zero risk.

## Acceptance Criteria

- [ ] ObjectMethodExecutor folder deleted
- [ ] All tests pass
- [ ] No compilation errors
- [ ] Git history preserved (not rewritten)

## Work Log

### 2026-01-20 - Issue Created

**By:** Claude Code (Simplicity Reviewer Agent)

**Actions:**
- Identified unused folder during dead code analysis
- Verified no references exist
- Recommended immediate deletion

### 2026-01-20 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready
