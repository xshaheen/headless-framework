---
status: done
priority: p1
issue_id: "017"
tags: [code-review, dotnet, async, features]
dependencies: []
---

# Add ConfigureAwait(false) to All Library Code

## Problem Statement

Framework.Features.* is library code that will be consumed by various applications, including those with synchronization contexts (WPF, WinForms, legacy ASP.NET). Every `await` in library code should have `ConfigureAwait(false)` to prevent potential deadlocks.

Currently, NONE of the await calls use `ConfigureAwait(false)`.

## Findings

- All async methods in Framework.Features.Core and Framework.Features.Storage.EntityFramework are missing ConfigureAwait(false)
- This is a systematic issue across ~30+ await statements
- Potential for deadlock when library is consumed by apps with SynchronizationContext

**Affected files:**
- `src/Framework.Features.Core/Values/FeatureManager.cs` - all async methods
- `src/Framework.Features.Core/Definitions/DynamicFeatureDefinitionStore.cs` - all async methods
- `src/Framework.Features.Core/Values/FeatureValueStore.cs` - all methods
- `src/Framework.Features.Storage.EntityFramework/EfFeatureValueRecordRecordRepository.cs` - all methods
- `src/Framework.Features.Core/Seeders/FeaturesInitializationBackgroundService.cs` - all methods

## Proposed Solutions

### Option 1: Add ConfigureAwait(false) to All Awaits

**Approach:** Systematically add `.ConfigureAwait(false)` to every await statement.

**Pros:**
- Standard practice for library code
- Prevents deadlocks in sync context scenarios
- Minimal performance overhead

**Cons:**
- Verbose code
- Need to remember for future code

**Effort:** 1-2 hours

**Risk:** Low

---

### Option 2: Use ConfigureAwait Source Generator

**Approach:** Use a source generator or analyzer that auto-adds ConfigureAwait(false).

**Pros:**
- Automatic enforcement
- Less manual work

**Cons:**
- Adds build dependency
- May not work in all scenarios

**Effort:** 2-3 hours

**Risk:** Low

## Recommended Action

*To be filled during triage.*

## Technical Details

**Affected files:**
- All `.cs` files in `src/Framework.Features.Core/`
- All `.cs` files in `src/Framework.Features.Storage.EntityFramework/`

**Pattern to change:**
```csharp
// Before
await someMethod(cancellationToken);

// After
await someMethod(cancellationToken).ConfigureAwait(false);
```

## Acceptance Criteria

- [x] All await calls in Framework.Features.Core have ConfigureAwait(false)
- [x] All await calls in Framework.Features.Storage.EntityFramework have ConfigureAwait(false)
- [x] Code compiles and tests pass
- [x] Consider adding analyzer to enforce in future code

## Work Log

### 2026-01-14 - Initial Discovery

**By:** Claude Code

**Actions:**
- Identified missing ConfigureAwait(false) across all library code
- Counted ~30+ await statements affected
- Reviewed .NET best practices for library code

**Learnings:**
- This is a common oversight in library code
- Critical for preventing deadlocks in sync context scenarios

## Notes

- This is a blocking issue for production use by WPF/WinForms consumers
- Consider using Meziantou.Analyzer to enforce this rule

### 2026-01-14 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready

### 2026-01-14 - Completed

**By:** Agent
**Actions:**
- Status changed: ready → done
