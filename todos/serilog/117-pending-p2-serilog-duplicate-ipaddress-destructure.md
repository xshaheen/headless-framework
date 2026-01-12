---
status: pending
priority: p2
issue_id: "117"
tags: [code-review, dotnet, serilog, code-duplication]
dependencies: []
---

# Duplicate IPAddress Destructuring in SerilogFactory

## Problem Statement

The `Destructure.ByTransforming<IPAddress?>` call is duplicated in both `ConfigureBootstrapLoggerConfiguration` and `ConfigureReloadableLoggerConfiguration`. If the destructuring logic needs to change, it must be updated in two places.

## Findings

**Source:** strict-dotnet-reviewer, pattern-recognition-specialist agents

**Affected Files:**
- `src/Framework.Logging.Serilog/SerilogFactory.cs:52`
- `src/Framework.Logging.Serilog/SerilogFactory.cs:122`

**Current Code:**
```csharp
// Line 52 (Bootstrap)
.Destructure.ByTransforming<IPAddress?>(ip => ip?.ToString() ?? "")

// Line 122 (Reloadable)
.Destructure.ByTransforming<IPAddress?>(ip => ip?.ToString() ?? "")
```

## Proposed Solutions

### Option 1: Extract to Private Constant Lambda (Recommended)
**Pros:** Single source of truth, clear intent
**Cons:** Minor refactor
**Effort:** Trivial
**Risk:** Low

```csharp
private static readonly Func<IPAddress?, string> _IpAddressTransform =
    ip => ip?.ToString() ?? string.Empty;

// Usage:
.Destructure.ByTransforming(_IpAddressTransform)
```

### Option 2: Extract to Private Extension Method
**Pros:** Fluent API preserved
**Cons:** More code
**Effort:** Small
**Risk:** Low

```csharp
private static LoggerConfiguration _AddIpAddressDestructure(
    this LoggerConfiguration config)
{
    return config.Destructure.ByTransforming<IPAddress?>(ip => ip?.ToString() ?? string.Empty);
}
```

## Technical Details

**Affected Components:** SerilogFactory
**Files to Modify:** 1 file

## Acceptance Criteria

- [ ] IPAddress destructuring defined in single location
- [ ] Both configurations use shared definition
- [ ] Code compiles without errors

## Work Log

| Date | Action | Learnings |
|------|--------|-----------|
| 2026-01-11 | Created from code review | DRY principle - avoid duplication |

## Resources

- Serilog Destructuring documentation
