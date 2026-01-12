---
status: pending
priority: p2
issue_id: "007"
tags: [code-review, security, multi-tenancy]
dependencies: []
---

# Public Filter Bypass Methods Without Audit Logging

## Problem Statement

Public extension methods allow bypassing security filters without any authorization check or audit logging. Any code with access to `IQueryable<T>` can bypass tenant isolation.

## Findings

### Location
- **File:** `src/Framework.Orm.EntityFramework/Contexts/IgnoreQueryFiltersExtensions.cs`
- **Lines:** 16-32

### Evidence
```csharp
public static IQueryable<TEntity> IgnoreMultiTenancyFilter<TEntity>(this IQueryable<TEntity> source)
public static IQueryable<TEntity> IgnoreNotDeletedFilter<TEntity>(this IQueryable<TEntity> source)
public static IQueryable<TEntity> IgnoreNotSuspendedFilter<TEntity>(this IQueryable<TEntity> source)
```

### Security Risk
- Any code with access to `IQueryable<T>` can bypass tenant isolation
- No authorization checks before filter bypass
- No audit logging of filter bypasses
- Developers can accidentally or intentionally bypass multi-tenancy protection

## Proposed Solutions

### Option 1: Add audit logging (Recommended)
Log every filter bypass call with context:
```csharp
public static IQueryable<TEntity> IgnoreMultiTenancyFilter<TEntity>(this IQueryable<TEntity> source)
{
    AuditLog.Log($"Multi-tenancy filter bypassed for {typeof(TEntity).Name}");
    return source.IgnoreQueryFilters([HeadlessQueryFilters.MultiTenancyFilter]);
}
```

**Pros:** Visibility into filter bypass usage
**Cons:** Requires logging infrastructure
**Effort:** Small
**Risk:** Low

### Option 2: Require explicit authorization context
```csharp
public static IQueryable<TEntity> IgnoreMultiTenancyFilter<TEntity>(
    this IQueryable<TEntity> source,
    IFilterBypassAuthorization auth)
{
    auth.AssertCanBypassTenantFilter();
    // ...
}
```

**Pros:** Enforces authorization
**Cons:** API change, adds friction
**Effort:** Medium
**Risk:** Medium

### Option 3: Make methods internal
Restrict access to framework code only.

**Pros:** Prevents accidental misuse
**Cons:** Limits legitimate use cases
**Effort:** Trivial
**Risk:** High (breaking change)

## Recommended Action
<!-- To be filled during triage -->

## Technical Details

### Affected Files
- `src/Framework.Orm.EntityFramework/Contexts/IgnoreQueryFiltersExtensions.cs`

### Affected Components
- Multi-tenant query execution
- Security audit trail

### Database Changes Required
None

## Acceptance Criteria
- [ ] Filter bypass calls are logged
- [ ] Security team can review bypass patterns
- [ ] No unauthorized cross-tenant access possible

## Work Log
| Date | Action | Learnings |
|------|--------|-----------|
| 2026-01-12 | Identified during security review | Security-sensitive operations need audit trail |

## Resources
- OWASP Broken Access Control
