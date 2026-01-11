---
status: pending
priority: p2
issue_id: "118"
tags: [code-review, dotnet, naming-conventions, serilog]
dependencies: []
---

# Extension Method Naming Convention Violation in SerilogFactory

## Problem Statement

Project conventions define `_PascalCase` for **private methods**, but `_WriteToConsole`, `_WriteToDebug`, `_WriteToLogFiles`, and `_File` are **extension methods** (they use `this` keyword). Extension methods must be static in a static class and are conceptually public, even if marked private.

The underscore prefix convention is meant for true private methods, not extension helpers.

## Findings

**Source:** strict-dotnet-reviewer agent

**Affected Files:**
- `src/Framework.Logging.Serilog/SerilogFactory.cs:153` - `_WriteToConsole`
- `src/Framework.Logging.Serilog/SerilogFactory.cs:162` - `_WriteToDebug`
- `src/Framework.Logging.Serilog/SerilogFactory.cs:169` - `_WriteToLogFiles`
- `src/Framework.Logging.Serilog/SerilogFactory.cs:190` - `_File`

**Current Code:**
```csharp
private static void _WriteToConsole(this LoggerConfiguration loggerConfiguration, ConsoleTheme theme)
private static void _WriteToDebug(this LoggerConfiguration loggerConfiguration)
private static void _WriteToLogFiles(this LoggerConfiguration loggerConfiguration, ITextFormatter textFormatter)
private static LoggerConfiguration _File(this LoggerSinkConfiguration config, ITextFormatter formatter, string path)
```

## Proposed Solutions

### Option 1: Convert to Regular Private Methods (Recommended)
**Pros:** Follows convention correctly, clearer semantics
**Cons:** Call sites change slightly
**Effort:** Small
**Risk:** Low

```csharp
// Remove 'this' keyword, keep underscore prefix for private methods
private static void _WriteToConsole(LoggerConfiguration loggerConfiguration, ConsoleTheme theme)
{
    loggerConfiguration.WriteTo.Console(...);
}

// Call as:
_WriteToConsole(loggerConfiguration, theme);
```

### Option 2: Keep as Extension, Remove Underscore
**Pros:** Maintains extension method syntax
**Cons:** Breaks naming convention for private helpers
**Effort:** Small
**Risk:** Low

```csharp
private static void WriteToConsole(this LoggerConfiguration loggerConfiguration, ConsoleTheme theme)
```

## Technical Details

**Affected Components:** SerilogFactory internal helpers
**Files to Modify:** 1 file

## Acceptance Criteria

- [ ] Extension methods either converted to regular methods with underscore OR have underscore removed
- [ ] Naming follows project conventions
- [ ] Code compiles without errors

## Work Log

| Date | Action | Learnings |
|------|--------|-----------|
| 2026-01-11 | Created from code review | Extension methods are conceptually public even if marked private |

## Resources

- CLAUDE.md naming conventions
