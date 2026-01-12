---
status: pending
priority: p1
issue_id: "003"
tags: [code-review, bug, vodafone, sms]
dependencies: []
---

# VodafoneSmsSender sends HttpRequestMessage as JSON body instead of using it

## Problem Statement

`VodafoneSmsSender.SendAsync` creates an `HttpRequestMessage` with XML content but then calls `PostAsJsonAsync` with the `requestMessage` as the body parameter, which serializes the `HttpRequestMessage` object as JSON instead of sending it as the request.

## Findings

- **File:** `src/Framework.Sms.Vodafone/VodafoneSmsSender.cs:39-42`
- **Current code:**
```csharp
using var requestMessage = new HttpRequestMessage(HttpMethod.Post, _uri);
requestMessage.Content = new StringContent(_BuildPayload(request), Encoding.UTF8, "application/xml");

var response = await _httpClient.PostAsJsonAsync(_uri, requestMessage, cancellationToken);
```
- The `HttpRequestMessage` object is serialized as JSON body
- The XML content in `requestMessage.Content` is never sent
- API will receive invalid payload

## Proposed Solutions

### Option 1: Use SendAsync instead of PostAsJsonAsync

**Approach:** Use `HttpClient.SendAsync` to properly send the constructed `HttpRequestMessage`.

```csharp
using var requestMessage = new HttpRequestMessage(HttpMethod.Post, _uri);
requestMessage.Content = new StringContent(_BuildPayload(request), Encoding.UTF8, "application/xml");

var response = await _httpClient.SendAsync(requestMessage, cancellationToken);
```

**Pros:**
- Correct HTTP request handling
- XML content will be sent properly

**Cons:**
- None

**Effort:** 5 minutes

**Risk:** Low

## Recommended Action

Fix immediately - this is a critical bug preventing Vodafone SMS from working correctly.

## Technical Details

**Affected files:**
- `src/Framework.Sms.Vodafone/VodafoneSmsSender.cs:42`

**Before:**
```csharp
var response = await _httpClient.PostAsJsonAsync(_uri, requestMessage, cancellationToken);
```

**After:**
```csharp
var response = await _httpClient.SendAsync(requestMessage, cancellationToken);
```

## Acceptance Criteria

- [ ] Use `SendAsync` instead of `PostAsJsonAsync`
- [ ] XML content is properly sent to Vodafone API
- [ ] Response parsing still works correctly

## Work Log

### 2026-01-12 - Code Review Discovery

**By:** Claude Code

**Actions:**
- Identified HTTP request construction bug
- `PostAsJsonAsync` with `HttpRequestMessage` as content is incorrect
- Should use `SendAsync` to send the constructed request
