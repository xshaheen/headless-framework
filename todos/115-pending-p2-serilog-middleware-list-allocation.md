---
status: pending
priority: p2
issue_id: "115"
tags: [code-review, dotnet, performance, serilog, middleware]
dependencies: []
---

# List Allocation in Hot Path - SerilogEnrichersMiddleware

## Problem Statement

`SerilogEnrichersMiddleware.InvokeAsync` creates a `List<ILogEventEnricher>` on every HTTP request, then uses the spread operator `[.. enrichers]` which creates another array allocation. This is unnecessary allocation in a hot path that runs on every request.

At 10K req/s, this creates ~20K allocations/second just for enricher handling.

## Findings

**Source:** performance-oracle, strict-dotnet-reviewer, code-simplicity-reviewer agents

**Affected Files:**
- `src/Framework.Api.Logging.Serilog/SerilogEnrichersMiddleware.cs:19` - `new List<ILogEventEnricher>()`
- `src/Framework.Api.Logging.Serilog/SerilogEnrichersMiddleware.cs:36` - `[.. enrichers]` spread operator

**Current Code:**
```csharp
public async Task InvokeAsync(HttpContext context, RequestDelegate next)
{
    var enrichers = new List<ILogEventEnricher>();  // Allocation 1

    if (requestContext.User.UserId is not null)
        enrichers.Add(new PropertyEnricher(_UserId, requestContext.User.UserId));
    // ...

    using (LogContext.Push([.. enrichers]))  // Allocation 2 (spread to array)
    {
        await next(context);
    }
}
```

## Proposed Solutions

### Option 1: Use Fixed-Size Array (Recommended)
**Pros:** Zero List overhead, single allocation of exact size needed
**Cons:** Slightly more verbose
**Effort:** Small
**Risk:** Low

```csharp
public async Task InvokeAsync(HttpContext context, RequestDelegate next)
{
    var userId = requestContext.User.UserId;
    var accountId = requestContext.User.AccountId;
    var correlationId = requestContext.CorrelationId;

    var count = (userId is not null ? 1 : 0)
              + (accountId is not null ? 1 : 0)
              + (correlationId is not null ? 1 : 0);

    if (count == 0)
    {
        await next(context).AnyContext();
        return;
    }

    var enrichers = new ILogEventEnricher[count];
    var index = 0;

    if (userId is not null)
        enrichers[index++] = new PropertyEnricher(_UserId, userId);
    if (accountId is not null)
        enrichers[index++] = new PropertyEnricher(_AccountId, accountId);
    if (correlationId is not null)
        enrichers[index++] = new PropertyEnricher(_CorrelationId, correlationId);

    using (LogContext.Push(enrichers))
    {
        await next(context).AnyContext();
    }
}
```

### Option 2: Use LogContext.PushProperty Directly
**Pros:** No array allocation at all, simplest
**Cons:** Multiple using statements
**Effort:** Small
**Risk:** Low

```csharp
public async Task InvokeAsync(HttpContext context, RequestDelegate next)
{
    using var _ = requestContext.User.UserId is { } userId
        ? LogContext.PushProperty(_UserId, userId) : null;
    using var __ = requestContext.User.AccountId is { } accountId
        ? LogContext.PushProperty(_AccountId, accountId) : null;
    using var ___ = requestContext.CorrelationId is { } correlationId
        ? LogContext.PushProperty(_CorrelationId, correlationId) : null;

    await next(context).AnyContext();
}
```

## Technical Details

**Affected Components:** SerilogEnrichersMiddleware
**Files to Modify:** 1 file

## Acceptance Criteria

- [ ] No List allocation per request
- [ ] No spread operator allocation
- [ ] Early exit when no enrichers needed
- [ ] Code compiles without errors
- [ ] Existing tests pass

## Work Log

| Date | Action | Learnings |
|------|--------|-----------|
| 2026-01-11 | Created from code review | Hot path allocations should be minimized |

## Resources

- Serilog LogContext.PushProperty documentation
