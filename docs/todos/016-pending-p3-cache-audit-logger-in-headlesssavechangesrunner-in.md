---
status: pending
priority: p3
issue_id: "016"
tags: ["code-review","performance","dotnet"]
dependencies: []
---

# Cache audit logger in HeadlessSaveChangesRunner instead of allocating per save

## Problem Statement

_GetAuditLogger(context) calls context.GetService<ILoggerFactory>()?.CreateLogger(context.GetType()) on every SaveChanges call. The old code cached the logger via a 'field ??=' backing field on the DbContext instance. ILoggerFactory.CreateLogger does a dictionary lookup and may allocate per call. Since HeadlessSaveChangesRunner is static and can't hold instance state, caching needs a different mechanism.

## Findings

- **Location:** src/Headless.Orm.EntityFramework/Contexts/HeadlessSaveChangesRunner.cs:269-272
- **Old cache:** HeadlessDbContext._AuditLogger (field ??= pattern, removed in refactor)
- **Discovered by:** strict-dotnet-reviewer

## Proposed Solutions

### Pass ILogger? as a parameter from the DbContext
- **Pros**: DbContext can still cache via field ??=, runner stays pure
- **Cons**: Adds one more parameter to already-long signature
- **Effort**: Small
- **Risk**: Low

### Static ConcurrentDictionary<Type, ILogger> cache in HeadlessSaveChangesRunner
- **Pros**: No call-site changes, zero allocations after first save per context type
- **Cons**: Static state, slight GC pressure for the dictionary
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Pass ILogger? from the DbContext (restore field ??= caching on the context). Both contexts had _AuditLogger before; restore it and pass it to ExecuteAsync.

## Acceptance Criteria

- [ ] Logger is not created on every SaveChanges call
- [ ] Logger is resolved at most once per DbContext instance (or once per context type for static cache)

## Notes

Source: Code review

## Work Log

### 2026-03-21 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
