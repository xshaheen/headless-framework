---
status: ready
priority: p1
issue_id: "001"
tags: ["code-review","architecture","dotnet"]
dependencies: []
---

# Fix Options.Create in AddHeadlessAuditLog — bypasses IConfiguration binding

## Problem Statement

Abstractions/Setup.cs:23 uses `services.TryAddSingleton(Options.Create(options))` which freezes options at registration time. Downstream consumers cannot bind options from appsettings.json via `services.Configure<AuditLogOptions>(config.GetSection("AuditLog"))`, use IOptionsMonitor for live reload, or override via environment variables. Every other Headless.* setup method uses `services.AddOptions<T>().Configure(...)` — this one is inconsistent and broken.

## Findings

- **Location:** src/Headless.AuditLog.Abstractions/Setup.cs:23
- **Severity:** P1 — options pipeline broken for all consumers
- **Discovered by:** strict-dotnet-reviewer

## Proposed Solutions

### Use AddOptions<T>().Configure pattern
- **Pros**: Consistent with all other Headless.* packages, enables IConfiguration binding and IOptionsMonitor
- **Cons**: None — this is the correct .NET pattern
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Replace `services.TryAddSingleton(Options.Create(options))` with `services.AddOptions<AuditLogOptions>(); if (configure is not null) services.Configure<AuditLogOptions>(configure);`

## Acceptance Criteria

- [ ] AddHeadlessAuditLog uses AddOptions<AuditLogOptions>() not Options.Create
- [ ] Consumer can bind from IConfiguration.GetSection
- [ ] Consumer can use IOptionsMonitor<AuditLogOptions>
- [ ] Consistent with other Headless.* setup methods

## Notes

Discovered during PR #187 code review. This is a P1 because every consumer of the framework will hit this when trying to configure audit options from appsettings.json.

## Work Log

### 2026-03-15 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin

### 2026-03-15 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready
