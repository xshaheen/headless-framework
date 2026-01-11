---
status: pending
priority: p1
issue_id: "137"
tags: [code-review, async, paymob, cashin]
dependencies: []
---

# Missing CancellationToken on All Async Methods

## Problem Statement

None of the async methods in Framework.Payments.Paymob.CashIn accept or propagate `CancellationToken`. This violates the project's coding standards (CLAUDE.md: "Always pass CancellationToken") and prevents proper request cancellation.

For a payment SDK, this is critical - users cannot cancel long-running or hung payment operations.

## Findings

- **All 15+ async methods missing CancellationToken**
- **Affected files**:
  - `PaymobCashInAuthenticator.cs`: RequestAuthenticationTokenAsync, GetAuthenticationTokenAsync
  - `PaymobCashInBroker.CreateOrder.cs`: CreateOrderAsync
  - `PaymobCashInBroker.Payment.cs`: CreateWalletPayAsync, CreateKioskPayAsync, CreateCashCollectionPayAsync, CreateSavedTokenPayAsync, _PayAsync
  - `PaymobCashInBroker.RequestPaymentKey.cs`: RequestPaymentKeyAsync
  - `PaymobCashInBroker.Intention.cs`: CreateIntentionAsync, RefundTransactionAsync, VoidTransactionAsync, _SendApiTokenPostAsync
  - `PaymobCashInBroker.TransactionsQueries.cs`: GetTransactionsPageAsync, GetTransactionAsync
  - `PaymobCashInBroker.OrderQueries.cs`: GetOrdersPageAsync, GetOrderAsync
  - `PaymobCashInException.cs`: ThrowAsync

## Proposed Solutions

### Option 1: Add CancellationToken to All Methods (Recommended)

**Approach:** Add `CancellationToken cancellationToken = default` parameter to all async methods and propagate it through all HTTP calls.

```csharp
// Interface change
Task<CashInCreateOrderResponse> CreateOrderAsync(
    CashInCreateOrderRequest request,
    CancellationToken cancellationToken = default);

// Implementation
public async Task<CashInCreateOrderResponse> CreateOrderAsync(
    CashInCreateOrderRequest request,
    CancellationToken cancellationToken = default)
{
    var authToken = await authenticator.GetAuthenticationTokenAsync(cancellationToken).AnyContext();
    // ...
    using var response = await httpClient
        .PostAsJsonAsync(requestUrl, internalRequest, _options.SerializationOptions, cancellationToken)
        .AnyContext();
    // ...
}
```

**Pros:**
- Standard .NET async pattern
- Enables proper request cancellation
- Follows project conventions

**Cons:**
- Breaking change to interface
- Requires updating all method signatures

**Effort:** 3-4 hours

**Risk:** Low (but breaking)

## Recommended Action

**To be filled during triage.**

## Technical Details

**Affected files:**
- `src/Framework.Payments.Paymob.CashIn/IPaymobCashInBroker.cs` (interface)
- `src/Framework.Payments.Paymob.CashIn/IPaymobCashInAuthenticator.cs` (interface)
- All `PaymobCashInBroker.*.cs` partial files
- `src/Framework.Payments.Paymob.CashIn/PaymobCashInAuthenticator.cs`
- `src/Framework.Payments.Paymob.CashIn/Models/PaymobCashInException.cs`

**HTTP calls to update:**
- `httpClient.PostAsJsonAsync` -> add cancellationToken
- `httpClient.SendAsync` -> add cancellationToken
- `response.Content.ReadFromJsonAsync` -> add cancellationToken
- `response.Content.ReadAsStringAsync` -> add cancellationToken

## Acceptance Criteria

- [ ] All async methods have CancellationToken parameter
- [ ] CancellationToken propagated to all HTTP operations
- [ ] Interface updated with new signatures
- [ ] Unit tests verify cancellation behavior
- [ ] No AnyContext() missing on any await

## Work Log

### 2026-01-11 - Initial Discovery

**By:** Claude Code (Code Review)

**Actions:**
- Identified all async methods missing CancellationToken
- Verified project convention requires CancellationToken
- Drafted solution approach

**Learnings:**
- Payment SDKs especially need cancellation support
- This is a breaking interface change
