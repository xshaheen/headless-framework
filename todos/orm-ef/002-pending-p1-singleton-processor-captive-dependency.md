---
status: pending
priority: p1
issue_id: "002"
tags: [code-review, architecture, dotnet, dependency-injection]
dependencies: []
---

# Singleton IHeadlessEntityModelProcessor with Scoped Dependencies

## Problem Statement

`IHeadlessEntityModelProcessor` is registered as **Singleton** but depends on `ICurrentTenant` and `ICurrentUser` which are typically Scoped in real applications. This creates a captive dependency anti-pattern.

If a user replaces `ICurrentTenant`/`ICurrentUser` with Scoped implementations (typical for per-request tenant resolution), the Singleton `HeadlessEntityModelProcessor` will capture a single instance and never see updated tenant/user values.

**Why it matters:** Multi-tenant applications may see cross-tenant data leakage or incorrect audit trails when `ICurrentTenant`/`ICurrentUser` are scoped but the processor is singleton.

## Findings

### Location
- **File:** `src/Framework.Orm.EntityFramework/Setup.cs`
- **Lines:** 60-64

### Evidence
```csharp
services.TryAddSingleton<IHeadlessEntityModelProcessor, HeadlessEntityModelProcessor>();
services.TryAddSingleton<IClock, Clock>();
services.TryAddSingleton<IGuidGenerator, SequentialAtEndGuidGenerator>();
services.TryAddSingleton<ICurrentTenant, NullCurrentTenant>();
services.TryAddSingleton<ICurrentUser, NullCurrentUser>();
```

The processor constructor captures dependencies:
```csharp
// HeadlessEntityModelProcessor.cs:26-31
public sealed class HeadlessEntityModelProcessor(
    ICurrentTenant currentTenant,
    ICurrentUser currentUser,
    IGuidGenerator guidGenerator,
    IClock clock
) : IHeadlessEntityModelProcessor
```

## Proposed Solutions

### Option 1: Change to Scoped registration (Recommended)
```csharp
services.TryAddScoped<IHeadlessEntityModelProcessor, HeadlessEntityModelProcessor>();
```

**Pros:** Correctly matches DbContext lifetime, prevents captive dependencies
**Cons:** Slightly more memory per request
**Effort:** Small
**Risk:** Low - processor is lightweight

### Option 2: Inject IServiceProvider, resolve per-call
```csharp
public ProcessBeforeSaveReport ProcessEntries(DbContext db)
{
    var currentTenant = _serviceProvider.GetRequiredService<ICurrentTenant>();
    // ...
}
```

**Pros:** Keeps singleton, always gets fresh values
**Cons:** Service locator anti-pattern, harder to test
**Effort:** Medium
**Risk:** Medium

### Option 3: Document behavior and add validation
Document that `ICurrentTenant`/`ICurrentUser` MUST be singletons when using the framework. Add startup validation.

**Pros:** No code change
**Cons:** Surprising constraint for users
**Effort:** Small
**Risk:** High - easy to misconfigure

## Recommended Action
<!-- To be filled during triage -->

## Technical Details

### Affected Files
- `src/Framework.Orm.EntityFramework/Setup.cs`

### Affected Components
- All HeadlessDbContext derivatives
- Multi-tenant applications

### Database Changes Required
None

## Acceptance Criteria
- [ ] Processor lifetime matches typical `ICurrentTenant`/`ICurrentUser` usage
- [ ] Tests verify correct tenant/user resolution per request
- [ ] Documentation updated with DI lifetime requirements

## Work Log
| Date | Action | Learnings |
|------|--------|-----------|
| 2026-01-12 | Identified during code review | Captive dependency anti-pattern |

## Resources
- Captive Dependency: https://blog.ploeh.dk/2014/06/02/captive-dependency/
