---
status: pending
priority: p3
issue_id: "121"
tags: [code-review, dotnet, serilog, configuration]
dependencies: []
---

# Hardcoded Log File Paths in SerilogFactory

## Problem Statement

Log file paths (`"Logs/bootstrap-.log"`, `"Logs/fatal-.log"`, etc.) are hardcoded. Users cannot configure the log directory without code changes. This is problematic for:
- Containerized deployments needing different paths
- Shared volume mounts
- Different deployment environments

## Findings

**Source:** strict-dotnet-reviewer, agent-native-reviewer agents

**Affected Files:**
- `src/Framework.Logging.Serilog/SerilogFactory.cs:70` - `"Logs/bootstrap-.log"`
- `src/Framework.Logging.Serilog/SerilogFactory.cs:175` - `"Logs/fatal-.log"`
- `src/Framework.Logging.Serilog/SerilogFactory.cs:180` - `"Logs/error-.log"`
- `src/Framework.Logging.Serilog/SerilogFactory.cs:185` - `"Logs/warning-.log"`

**Additional Hardcoded Settings:**
- `retainedFileCountLimit: 5`
- `rollingInterval: RollingInterval.Day`
- `flushToDiskInterval: TimeSpan.FromSeconds(1)`

## Proposed Solutions

### Option 1: Add LoggingOptions Configuration (Recommended)
**Pros:** Full configurability
**Cons:** More code
**Effort:** Medium
**Risk:** Low

```csharp
public class LoggingFileOptions
{
    public string LogDirectory { get; set; } = "Logs";
    public int RetainedFileCount { get; set; } = 5;
    public RollingInterval RollingInterval { get; set; } = RollingInterval.Day;
    public TimeSpan FlushInterval { get; set; } = TimeSpan.FromSeconds(1);
}
```

### Option 2: Make LogDirectory a Parameter
**Pros:** Simple, minimal change
**Cons:** Only solves directory, not other settings
**Effort:** Small
**Risk:** Low

```csharp
public static LoggerConfiguration ConfigureBootstrapLoggerConfiguration(
    this LoggerConfiguration loggerConfiguration,
    string logDirectory = "Logs")
```

### Option 3: Read from Serilog Config Section
**Pros:** Uses existing Serilog configuration mechanism
**Cons:** Bootstrap logger might not have config available yet
**Effort:** Small
**Risk:** Medium

## Technical Details

**Affected Components:** SerilogFactory file sink configuration
**Files to Modify:** 1-2 files

## Acceptance Criteria

- [ ] Log directory is configurable
- [ ] Default behavior unchanged for existing users
- [ ] Documentation updated

## Work Log

| Date | Action | Learnings |
|------|--------|-----------|
| 2026-01-11 | Created from code review | Hardcoded paths limit deployment flexibility |

## Resources

- Serilog File Sink documentation
