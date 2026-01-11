---
status: pending
priority: p3
issue_id: "124"
tags: [code-review, dotnet, serilog, architecture]
dependencies: []
---

# Duplicate CorrelationId Enrichment

## Problem Statement

CorrelationId is enriched in two places:
1. `ApiSerilogFactory.ConfigureApiLoggerConfiguration` adds `.Enrich.WithCorrelationId()` (from Serilog.Enrichers.ClientInfo)
2. `SerilogEnrichersMiddleware` pushes `CorrelationId` from `IRequestContext`

This may cause conflicts or duplicate log properties.

## Findings

**Source:** architecture-strategist agent

**Affected Files:**
- `src/Framework.Api.Logging.Serilog/ApiSerilogFactory.cs:84` - `.Enrich.WithCorrelationId()`
- `src/Framework.Api.Logging.Serilog/SerilogEnrichersMiddleware.cs:31-34` - `new PropertyEnricher(_CorrelationId, requestContext.CorrelationId)`

**Questions:**
- Which CorrelationId source is authoritative - header-based (`Serilog.Enrichers.ClientInfo`) or `IRequestContext`?
- Do they produce the same value?
- Does having both cause issues?

## Proposed Solutions

### Option 1: Remove from ApiSerilogFactory (Recommended if IRequestContext is authoritative)
**Pros:** Single source of truth
**Cons:** Requires IRequestContext to be properly populated
**Effort:** Trivial
**Risk:** Low

### Option 2: Remove from Middleware (if header-based is authoritative)
**Pros:** Single source of truth
**Cons:** May lose IRequestContext's correlation ID
**Effort:** Trivial
**Risk:** Low

### Option 3: Document Behavior
**Pros:** No code change
**Cons:** Potential confusion
**Effort:** Trivial
**Risk:** Low

## Technical Details

**Affected Components:** ApiSerilogFactory, SerilogEnrichersMiddleware
**Files to Modify:** 1 file (whichever is removed)

## Acceptance Criteria

- [ ] Single source for CorrelationId enrichment
- [ ] Document which source is authoritative
- [ ] Verify no duplicate log properties

## Work Log

| Date | Action | Learnings |
|------|--------|-----------|
| 2026-01-11 | Created from architecture review | Duplicate enrichment can cause confusion |

## Resources

- Serilog.Enrichers.ClientInfo documentation
- IRequestContext implementation
