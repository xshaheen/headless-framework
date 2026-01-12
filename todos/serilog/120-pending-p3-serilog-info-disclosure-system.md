---
status: pending
priority: p3
issue_id: "120"
tags: [code-review, security, serilog, information-disclosure]
dependencies: []
---

# Information Disclosure - System Internals in Log Enrichers

## Problem Statement

Logs expose sensitive operational data that aids reconnaissance for targeted attacks:
- `EnvironmentUserName`: OS user running the application
- `MachineName`: Server hostname
- `ProcessId`/`ProcessName`: Process details
- `CommitHash`: Exact code version (helps attackers find known vulnerabilities)

## Findings

**Source:** security-sentinel agent

**Affected Files:**
- `src/Framework.Logging.Serilog/SerilogFactory.cs:53-58` (bootstrap enrichers)
- `src/Framework.Logging.Serilog/SerilogFactory.cs:125-133` (reloadable enrichers)

**Current Code:**
```csharp
.Enrich.WithEnvironmentName()
.Enrich.WithEnvironmentUserName()  // OS username running the process
.Enrich.WithProcessId()
.Enrich.WithProcessName()
.Enrich.WithMachineName()
.Enrich.WithProperty("CommitHash", AssemblyInformation.Entry.CommitNumber)
```

## Proposed Solutions

### Option 1: Make Sensitive Enrichers Configurable (Recommended)
**Pros:** Flexibility for production vs development
**Cons:** More configuration
**Effort:** Medium
**Risk:** Low

```csharp
public class LoggingEnricherOptions
{
    public bool IncludeEnvironmentUserName { get; set; } = false; // Off by default
    public bool IncludeMachineName { get; set; } = true;
    public bool IncludeCommitHash { get; set; } = true;
}
```

### Option 2: Remove EnvironmentUserName
**Pros:** Simple, removes most sensitive info
**Cons:** Less flexibility
**Effort:** Trivial
**Risk:** Low

### Option 3: Accept Risk with Documentation
**Pros:** No code change
**Cons:** Info disclosure remains
**Effort:** None
**Risk:** Low (if log access is restricted)

Document that logs contain system information and should be properly secured.

## Technical Details

**Affected Components:** SerilogFactory enricher configuration
**Information Exposed:**
- `EnvironmentUserName`: Rarely needed for debugging
- `MachineName`: Useful for distributed systems
- `CommitHash`: Useful for debugging, but reveals version

## Acceptance Criteria

- [ ] Review which enrichers are truly necessary
- [ ] Either make configurable OR document security implications
- [ ] Ensure log access is properly restricted in deployment docs

## Work Log

| Date | Action | Learnings |
|------|--------|-----------|
| 2026-01-11 | Created from security review | Logs can reveal attack surface info |

## Resources

- OWASP Logging Guide
