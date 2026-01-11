---
status: pending
priority: p3
issue_id: "150"
tags: [code-review, duplication, paymob, cashin]
dependencies: []
---

# Inconsistent HTTP Request Patterns (Code Duplication)

## Problem Statement

Three different patterns are used for HTTP requests across the broker files. This creates inconsistency and code duplication, making maintenance harder.

## Findings

**Pattern 1** - `PostAsJsonAsync` (simplest):
```csharp
// PaymobCashInBroker.CreateOrder.cs line 19
using var response = await httpClient.PostAsJsonAsync(requestUrl, internalRequest, _options.SerializationOptions);
```

**Pattern 2** - Manual `HttpRequestMessage` with `Token` auth:
```csharp
// PaymobCashInBroker.Intention.cs lines 39-43
using var httpRequestMessage = new HttpRequestMessage(HttpMethod.Post, url);
httpRequestMessage.Headers.Add("Authorization", $"Token {_options.SecretKey}");
httpRequestMessage.Content = JsonContent.Create(request, options: _options.SerializationOptions);
```

**Pattern 3** - Manual `HttpRequestMessage` with `Bearer` auth (4 occurrences):
```csharp
// PaymobCashInBroker.TransactionsQueries.cs lines 22-27
using var requestMessage = new HttpRequestMessage();
requestMessage.Method = HttpMethod.Get;
requestMessage.RequestUri = new Uri(requestUrl, UriKind.Absolute);
requestMessage.Headers.Add("Authorization", $"Bearer {authToken}");
```

**Same error handling pattern repeated ~10 times:**
```csharp
if (!response.IsSuccessStatusCode)
{
    await PaymobCashInException.ThrowAsync(response);
}
```

## Proposed Solutions

### Option 1: Extract Helper Methods (Recommended)

**Approach:** Create private helper methods for each HTTP pattern.

```csharp
private async Task<TResponse> _PostWithAuthAsync<TRequest, TResponse>(
    string url,
    TRequest request,
    CancellationToken cancellationToken = default)
{
    var authToken = await authenticator.GetAuthenticationTokenAsync(cancellationToken).AnyContext();
    using var response = await httpClient
        .PostAsJsonAsync(url, request, _options.SerializationOptions, cancellationToken)
        .AnyContext();

    await response.EnsureSuccessOrThrowPaymobAsync(cancellationToken).AnyContext();

    return (await response.Content.ReadFromJsonAsync<TResponse>(_options.DeserializationOptions, cancellationToken).AnyContext())!;
}

private async Task<TResponse?> _GetWithBearerAuthAsync<TResponse>(
    string url,
    CancellationToken cancellationToken = default)
{
    var authToken = await authenticator.GetAuthenticationTokenAsync(cancellationToken).AnyContext();
    using var request = new HttpRequestMessage(HttpMethod.Get, url);
    request.Headers.Add("Authorization", $"Bearer {authToken}");

    using var response = await httpClient.SendAsync(request, cancellationToken).AnyContext();
    await response.EnsureSuccessOrThrowPaymobAsync(cancellationToken).AnyContext();

    return await response.Content.ReadFromJsonAsync<TResponse>(_options.DeserializationOptions, cancellationToken).AnyContext();
}
```

**Pros:**
- DRY code
- Consistent error handling
- Easier to add CancellationToken/AnyContext

**Cons:**
- Initial refactoring effort

**Effort:** 2-3 hours

**Risk:** Low

## Recommended Action

**To be filled during triage.**

## Technical Details

**Affected files:**
- `src/Framework.Payments.Paymob.CashIn/PaymobCashInBroker.CreateOrder.cs`
- `src/Framework.Payments.Paymob.CashIn/PaymobCashInBroker.Payment.cs`
- `src/Framework.Payments.Paymob.CashIn/PaymobCashInBroker.RequestPaymentKey.cs`
- `src/Framework.Payments.Paymob.CashIn/PaymobCashInBroker.Intention.cs`
- `src/Framework.Payments.Paymob.CashIn/PaymobCashInBroker.TransactionsQueries.cs`
- `src/Framework.Payments.Paymob.CashIn/PaymobCashInBroker.OrderQueries.cs`

## Acceptance Criteria

- [ ] HTTP patterns unified via helper methods
- [ ] Error handling consistent across all methods
- [ ] Code duplication reduced

## Work Log

### 2026-01-11 - Initial Discovery

**By:** Claude Code (Code Review)

**Actions:**
- Identified 3 different HTTP patterns
- Counted ~10 repeated error handling blocks
- Drafted helper method extraction
