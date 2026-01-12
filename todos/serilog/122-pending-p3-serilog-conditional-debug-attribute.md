---
status: pending
priority: p3
issue_id: "122"
tags: [code-review, dotnet, serilog, code-quality]
dependencies: []
---

# Use [Conditional] Attribute Instead of #if DEBUG

## Problem Statement

`_WriteToDebug` uses `#if DEBUG` preprocessor directive which means the method body is empty in Release builds but the method is still called. Using `[Conditional("DEBUG")]` attribute would make the call site itself conditional - the method won't even be called in Release builds.

## Findings

**Source:** strict-dotnet-reviewer agent

**Affected Files:**
- `src/Framework.Logging.Serilog/SerilogFactory.cs:162-167`

**Current Code:**
```csharp
private static void _WriteToDebug(this LoggerConfiguration loggerConfiguration)
{
#if DEBUG
    loggerConfiguration.WriteTo.Debug(outputTemplate: OutputTemplate, formatProvider: CultureInfo.InvariantCulture);
#endif
}
```

## Proposed Solutions

### Option 1: Use [Conditional] Attribute (Recommended)
**Pros:** Call site eliminated in Release, cleaner semantics
**Cons:** None
**Effort:** Trivial
**Risk:** Low

```csharp
[Conditional("DEBUG")]
private static void WriteToDebug(LoggerConfiguration loggerConfiguration)
{
    loggerConfiguration.WriteTo.Debug(
        outputTemplate: OutputTemplate,
        formatProvider: CultureInfo.InvariantCulture);
}
```

Note: Extension methods cannot use `[Conditional]`, so this requires converting to regular method (aligns with issue #118).

### Option 2: Keep #if DEBUG
**Pros:** No change needed
**Cons:** Empty method call in Release
**Effort:** None
**Risk:** None (micro-optimization)

## Technical Details

**Affected Components:** SerilogFactory debug sink
**Files to Modify:** 1 file
**Dependencies:** Should be done after #118 (extension method naming)

## Acceptance Criteria

- [ ] Debug sink uses `[Conditional]` attribute OR #if DEBUG kept with documentation
- [ ] Code compiles without errors in both Debug and Release

## Work Log

| Date | Action | Learnings |
|------|--------|-----------|
| 2026-01-11 | Created from code review | [Conditional] eliminates call site in Release |

## Resources

- https://docs.microsoft.com/en-us/dotnet/api/system.diagnostics.conditionalattribute
