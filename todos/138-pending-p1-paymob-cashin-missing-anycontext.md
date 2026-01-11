---
status: pending
priority: p1
issue_id: "138"
tags: [code-review, async, paymob, cashin]
dependencies: ["137"]
---

# Missing AnyContext() on All Await Calls

## Problem Statement

Per the project's CLAUDE.md: "Use `AnyContext()` extension (replaces `ConfigureAwait(false)`)." None of the async methods in Framework.Payments.Paymob.CashIn use this extension, which can cause deadlocks in synchronization context scenarios.

## Findings

- **All await calls missing `.AnyContext()`**
- Every `await` in the package needs to be updated
- Current pattern:
  ```csharp
  using var response = await _httpClient.PostAsJsonAsync(requestUrl, request, config.SerializationOptions);
  ```
- Should be:
  ```csharp
  using var response = await _httpClient
      .PostAsJsonAsync(requestUrl, request, config.SerializationOptions, cancellationToken)
      .AnyContext();
  ```

**Affected files:**
- `PaymobCashInAuthenticator.cs`: 4 awaits
- `PaymobCashInBroker.CreateOrder.cs`: 3 awaits
- `PaymobCashInBroker.Payment.cs`: 2 awaits
- `PaymobCashInBroker.RequestPaymentKey.cs`: 3 awaits
- `PaymobCashInBroker.Intention.cs`: 4 awaits
- `PaymobCashInBroker.TransactionsQueries.cs`: 4 awaits
- `PaymobCashInBroker.OrderQueries.cs`: 4 awaits
- `PaymobCashInException.cs`: 1 await

**Total: ~25 await statements need updating**

## Proposed Solutions

### Option 1: Add AnyContext() to All Awaits (Recommended)

**Approach:** Update all await statements to include `.AnyContext()`.

**Pros:**
- Prevents deadlocks in UI/ASP.NET synchronization contexts
- Follows project convention
- Straightforward change

**Cons:**
- Mechanical change across many files
- Should be done together with CancellationToken addition

**Effort:** 1-2 hours (combine with CancellationToken changes)

**Risk:** Low

## Recommended Action

**To be filled during triage.**

## Technical Details

**Affected files:**
- All source files in `src/Framework.Payments.Paymob.CashIn/`

**Pattern to apply:**
```csharp
// Before
var x = await SomeAsync();

// After
var x = await SomeAsync(cancellationToken).AnyContext();
```

## Acceptance Criteria

- [ ] All await statements have .AnyContext()
- [ ] CancellationToken passed to all awaited calls
- [ ] No synchronization context issues in tests

## Work Log

### 2026-01-11 - Initial Discovery

**By:** Claude Code (Code Review)

**Actions:**
- Identified all await calls missing AnyContext()
- Counted ~25 statements to update

**Learnings:**
- Best to combine with CancellationToken addition
