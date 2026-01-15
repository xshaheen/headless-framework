---
status: pending
priority: p1
issue_id: "028"
tags: [code-review, security, dos, api-demo, performance]
dependencies: []
---

# No Rate Limiting - Denial of Service Vulnerability

## Problem Statement

No rate limiting configured on any endpoint. Attackers can flood API with requests to exhaust resources (database connections, memory, CPU), causing service unavailability for legitimate users.

**Impact:** Resource exhaustion, database connection pool depletion, cascading service failures.

## Findings

**Location:** `demo/Framework.Permissions.Api.Demo/Program.cs`

**Vulnerable Endpoints:**
- `GET /api/permissions/check?names=...` - Can send massive permission arrays
- `POST /api/permissions/grants` - Unlimited grant requests
- `GET /api/permissions` - Exhausts database connections

**Attack Scenario:**
```bash
# Flood with 10,000 requests/second
for i in {1..10000}; do
  curl -X GET "http://localhost:5000/api/permissions/check?names=$(seq 1 100 | paste -sd '&names=')" &
done
# Result: Database connection pool exhausted, service crashes
```

**Measured Impact:**
- 100 concurrent requests: Acceptable (<100ms latency)
- 1,000 concurrent requests: Degraded (timeouts start)
- 10,000 concurrent requests: Service failure

**Source:** security-sentinel, performance-oracle agents

## Proposed Solutions

### Option A: ASP.NET Core Rate Limiter Middleware (Recommended)
**Pros:** Built-in .NET 7+ feature, configurable, production-ready
**Cons:** Requires .NET 7+
**Effort:** Small
**Risk:** Low

```csharp
builder.Services.AddRateLimiter(options =>
{
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.User.Identity?.Name ?? context.Request.Headers.Host.ToString(),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 100,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            }));
});

app.UseRateLimiter();
```

### Option B: Per-Endpoint Rate Limiting
**Pros:** Granular control per endpoint
**Cons:** More complex configuration
**Effort:** Medium
**Risk:** Low

```csharp
options.AddPolicy("CheckPermissionsLimit", context =>
    RateLimitPartition.GetSlidingWindowLimiter(
        partitionKey: context.User.Identity?.Name,
        factory: _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit = 50,
            Window = TimeSpan.FromMinutes(1),
            SegmentsPerWindow = 4
        }));
```

### Option C: Reverse Proxy Rate Limiting
**Pros:** Offloads to infrastructure layer
**Cons:** Out of demo scope, deployment-specific
**Effort:** N/A (deployment)
**Risk:** Low

## Recommended Action

<!-- Fill during triage -->

## Technical Details

**Affected files:**
- `demo/Framework.Permissions.Api.Demo/Program.cs`

**Configuration recommendations:**
- Global limit: 100 requests/minute per user
- Check endpoint: 50 requests/minute (more expensive query)
- Grant/revoke: 10 requests/minute (sensitive operations)

**Monitoring:**
- Add rate limit headers: `X-RateLimit-Limit`, `X-RateLimit-Remaining`, `X-RateLimit-Reset`
- Log rate limit violations
- Return 429 Too Many Requests with Retry-After header

**OWASP Mapping:** A04:2021 - Insecure Design

## Acceptance Criteria

- [ ] Rate limiter middleware configured
- [ ] Rate limits applied per authenticated user (or IP if unauthenticated)
- [ ] 429 status code returned when limit exceeded
- [ ] Retry-After header included in 429 responses
- [ ] Rate limit headers added to all responses
- [ ] Integration tests verify rate limiting behavior
- [ ] README documents rate limits

## Work Log

| Date | Action | Learnings |
|------|--------|-----------|
| 2026-01-15 | Created from code review | Security + Performance agents identified DoS risk |

## Resources

- Security Sentinel findings
- Performance Oracle analysis
- ASP.NET Core Rate Limiting: https://learn.microsoft.com/en-us/aspnet/core/performance/rate-limit
