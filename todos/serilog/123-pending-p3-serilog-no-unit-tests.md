---
status: pending
priority: p3
issue_id: "123"
tags: [code-review, testing, serilog]
dependencies: []
---

# No Unit Tests for Logging Packages

## Problem Statement

Neither `Framework.Logging.Serilog` nor `Framework.Api.Logging.Serilog` have unit test projects. This makes it difficult to verify:
- Enrichers are correctly configured
- File paths are correct
- Middleware enriches LogContext properly
- Configuration options work as expected

## Findings

**Source:** Code review - no test directories found

**Affected Packages:**
- `Framework.Logging.Serilog` - no tests
- `Framework.Api.Logging.Serilog` - no tests

## Proposed Solutions

### Option 1: Add Unit Test Projects (Recommended)
**Pros:** Regression protection, documentation through tests
**Cons:** Time investment
**Effort:** Medium
**Risk:** Low

Tests to add:
1. `SerilogEnrichersMiddlewareTests` - verify UserId, AccountId, CorrelationId enrichment
2. `SerilogFactoryTests` - verify enricher configuration
3. `ApiSerilogFactoryTests` - verify API-specific enrichers

### Option 2: Integration Tests Only
**Pros:** Tests real behavior
**Cons:** Slower, harder to isolate
**Effort:** Medium
**Risk:** Low

### Option 3: Accept No Tests
**Pros:** No effort
**Cons:** No regression protection
**Effort:** None
**Risk:** Medium

## Technical Details

**Test Project Names:**
- `Framework.Logging.Serilog.Tests.Unit`
- `Framework.Api.Logging.Serilog.Tests.Unit`

**Key Test Cases:**
```csharp
// Middleware test example
[Fact]
public async Task should_enrich_with_user_id_when_present()
{
    var requestContext = Substitute.For<IRequestContext>();
    requestContext.User.UserId.Returns("user-123");

    // ... verify LogContext contains UserId
}
```

## Acceptance Criteria

- [ ] Test projects created for both packages
- [ ] Middleware enrichment tested
- [ ] Factory configuration tested
- [ ] Tests follow project naming conventions

## Work Log

| Date | Action | Learnings |
|------|--------|-----------|
| 2026-01-11 | Created from code review | Logging is infrastructure that benefits from tests |

## Resources

- Serilog testing patterns
- xUnit, NSubstitute documentation
