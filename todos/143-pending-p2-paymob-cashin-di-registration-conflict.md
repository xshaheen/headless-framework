---
status: pending
priority: p2
issue_id: "143"
tags: [code-review, di, paymob, cashin]
dependencies: []
---

# Singleton + AddHttpClient Registration Conflict

## Problem Statement

The DI registration calls `AddSingleton` then `AddHttpClient` for the same type. `AddHttpClient<TClient, TImplementation>` registers as **Transient** by default, but the `AddSingleton` registration may conflict with or override this behavior.

## Findings

- **Location**: `src/Framework.Payments.Paymob.CashIn/AddPaymobCashInExtensions.cs:80-86`
- **Code**:
  ```csharp
  services
      .AddSingleton<IPaymobCashInAuthenticator, PaymobCashInAuthenticator>()
      .AddHttpClient<IPaymobCashInAuthenticator, PaymobCashInAuthenticator>(clientName);
  ```
- `AddHttpClient<TClient, TImpl>` registers the service with Transient lifetime
- The preceding `AddSingleton` creates a conflict
- If Singleton wins, the HttpClient injected at construction will be the same instance forever - defeating the purpose of `IHttpClientFactory` which manages handler lifetimes

## Proposed Solutions

### Option 1: Use IHttpClientFactory in Singleton (Recommended)

**Approach:** Inject `IHttpClientFactory` and create clients per-request.

```csharp
// Registration
services.AddHttpClient(clientName);  // Register named client only
services.AddSingleton<IPaymobCashInAuthenticator, PaymobCashInAuthenticator>();

// In PaymobCashInAuthenticator
public sealed class PaymobCashInAuthenticator : IPaymobCashInAuthenticator
{
    private readonly IHttpClientFactory _httpClientFactory;
    private const string ClientName = "paymob_cash_in";

    public PaymobCashInAuthenticator(IHttpClientFactory httpClientFactory, ...)
    {
        _httpClientFactory = httpClientFactory;
    }

    private HttpClient CreateClient() => _httpClientFactory.CreateClient(ClientName);
}
```

**Pros:**
- Proper HttpClient lifetime management
- Handler pooling works correctly
- Singleton token caching preserved

**Cons:**
- Requires refactoring to use factory

**Effort:** 1-2 hours

**Risk:** Low

---

### Option 2: Let AddHttpClient Control Lifetime

**Approach:** Remove `AddSingleton`, let `AddHttpClient` handle registration.

```csharp
// Remove AddSingleton, just use AddHttpClient
services.AddHttpClient<IPaymobCashInAuthenticator, PaymobCashInAuthenticator>(clientName);
```

**Pros:**
- Simpler registration

**Cons:**
- Authenticator becomes Transient, losing token caching benefit
- Would need to move caching elsewhere

**Effort:** N/A - Not recommended

**Risk:** High (loses caching)

## Recommended Action

**To be filled during triage.**

## Technical Details

**Affected files:**
- `src/Framework.Payments.Paymob.CashIn/AddPaymobCashInExtensions.cs:80-86`
- `src/Framework.Payments.Paymob.CashIn/PaymobCashInAuthenticator.cs` (if refactoring to IHttpClientFactory)

## Acceptance Criteria

- [ ] HttpClient lifetime properly managed by IHttpClientFactory
- [ ] Token caching preserved in Singleton
- [ ] No DI registration conflicts
- [ ] Handler pooling works correctly

## Work Log

### 2026-01-11 - Initial Discovery

**By:** Claude Code (Code Review)

**Actions:**
- Identified AddSingleton + AddHttpClient conflict
- Analyzed IHttpClientFactory usage patterns
- Drafted factory-based solution
