---
status: pending
priority: p1
issue_id: "005"
tags: [code-review, concurrency, cequens, sms, thread-safety]
dependencies: []
---

# CequensSmsSender mutates DefaultRequestHeaders on shared HttpClient

## Problem Statement

`CequensSmsSender.SendAsync` sets `httpClient.DefaultRequestHeaders.Authorization` on every call. Since `HttpClient` is typically shared (via `IHttpClientFactory`), this mutates shared state and is not thread-safe.

## Findings

- **File:** `src/Framework.Sms.Cequens/CequensSmsSender.cs:36`
- **Problematic code:**
```csharp
httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwtToken);
```
- Under concurrent requests:
  1. Thread A sets Authorization to TokenA
  2. Thread B sets Authorization to TokenB
  3. Thread A sends request with TokenB (wrong token!)
- `DefaultRequestHeaders` is NOT thread-safe for writes

## Proposed Solutions

### Option 1: Use per-request headers

**Approach:** Set Authorization on individual `HttpRequestMessage` instead of `DefaultRequestHeaders`.

```csharp
using var request = new HttpRequestMessage(HttpMethod.Post, _options.SingleSmsEndpoint);
request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwtToken);
request.Content = JsonContent.Create(apiRequest);

var response = await httpClient.SendAsync(request, cancellationToken);
```

**Pros:**
- Thread-safe
- No shared state mutation
- Standard pattern for typed HttpClient

**Cons:**
- Slightly more verbose

**Effort:** 30 minutes

**Risk:** Low

## Recommended Action

Implement Option 1 - use per-request headers instead of modifying `DefaultRequestHeaders`.

## Technical Details

**Affected files:**
- `src/Framework.Sms.Cequens/CequensSmsSender.cs:36`

**Before:**
```csharp
httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwtToken);
var response = await httpClient.PostAsJsonAsync(_options.SingleSmsEndpoint, apiRequest, cancellationToken);
```

**After:**
```csharp
using var request = new HttpRequestMessage(HttpMethod.Post, _options.SingleSmsEndpoint);
request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwtToken);
request.Content = JsonContent.Create(apiRequest);
var response = await httpClient.SendAsync(request, cancellationToken);
```

## Acceptance Criteria

- [ ] Authorization header set per-request, not on DefaultRequestHeaders
- [ ] No mutation of shared HttpClient state
- [ ] Works correctly under concurrent load

## Work Log

### 2026-01-12 - Code Review Discovery

**By:** Claude Code

**Actions:**
- Identified thread-safety issue with DefaultRequestHeaders
- Confirmed HttpClient is shared via IHttpClientFactory
- Proposed per-request header solution
