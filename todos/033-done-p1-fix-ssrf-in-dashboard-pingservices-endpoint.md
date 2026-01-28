---
status: done
priority: p1
issue_id: "033"
tags: [security, ssrf, dashboard]
dependencies: []
---

# Fix SSRF vulnerability in Dashboard PingServices endpoint

## Problem Statement

`RouteActionProvider.cs:495-528` allows SSRF. The `PingServices` endpoint accepts an `endpoint` query parameter and makes HTTP requests to arbitrary URLs without validation.

**File:** `src/Headless.Messaging.Dashboard/RouteActionProvider.cs`

```csharp
public async Task PingServices(HttpContext httpContext)
{
    var endpoint = httpContext.Request.Query["endpoint"];
    using var httpClient = new HttpClient();
    var healthEndpoint = endpoint + _options.PathMatch + "/api/health";
    var response = await httpClient.GetStringAsync(healthEndpoint);
    // ...
}
```

**Risks:**
1. Attackers can probe internal networks (e.g., `?endpoint=http://169.254.169.254` for cloud metadata)
2. Port scanning internal services
3. Information leakage via exception messages

## Findings

- **Severity:** CRITICAL (P1)
- **Impact:** SSRF allows attackers to access internal services, cloud metadata, and scan internal networks
- **Exploitability:** High - endpoint is always anonymous via `AllowAnonymous()`

## Proposed Solutions

### Option 1: Allowlist known nodes (Recommended)
Validate endpoint against registered node discovery addresses only.
- **Pros**: Tight security, only legitimate cluster nodes allowed
- **Cons**: Requires node registration infrastructure
- **Effort**: Medium
- **Risk**: Low

### Option 2: URL validation with blocklist
Block internal IP ranges (10.x, 172.16-31.x, 192.168.x, 169.254.x, localhost)
- **Pros**: Quick to implement
- **Cons**: Blocklists can be bypassed (DNS rebinding, IPv6)
- **Effort**: Small
- **Risk**: Medium

## Recommended Action

Implement Option 1. The endpoint should ONLY ping known cluster nodes from node discovery.

## Acceptance Criteria

- [ ] Validate `endpoint` parameter against allowlist of known nodes
- [ ] Add request timeout (5s max)
- [ ] Sanitize error messages to prevent information leakage
- [ ] Add unit tests for SSRF prevention

## Notes

Source: Security Sentinel agent code review

## Work Log

### 2026-01-25 - Created

**By:** Code Review Agent
**Actions:**
- Created from multi-agent code review findings
