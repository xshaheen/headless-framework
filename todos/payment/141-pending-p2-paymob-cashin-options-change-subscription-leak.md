---
status: pending
priority: p2
issue_id: "141"
tags: [code-review, resource-leak, paymob, cashin]
dependencies: []
---

# IOptionsMonitor.OnChange Subscription Not Disposed

## Problem Statement

The `PaymobCashInAuthenticator` subscribes to `IOptionsMonitor.OnChange` but never disposes the returned `IDisposable`. The callback will keep firing even after the authenticator is no longer in use.

## Findings

- **Location**: `src/Framework.Payments.Paymob.CashIn/PaymobCashInAuthenticator.cs:29-33`
- **Code**:
  ```csharp
  options.OnChange(_ =>
  {
      _cachedToken = null;
      _tokenExpiration = DateTimeOffset.MinValue;
  });
  // IDisposable returned by OnChange is not stored
  ```
- `OnChange` returns an `IDisposable` that should be disposed
- The class is Singleton so the issue is minor, but it's still a resource leak

## Proposed Solutions

### Option 1: Store and Dispose Subscription (Recommended)

**Approach:** Store the subscription and implement `IDisposable`.

```csharp
public sealed class PaymobCashInAuthenticator : IPaymobCashInAuthenticator, IDisposable
{
    private readonly IDisposable? _optionsChangeSubscription;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);  // From issue #136

    public PaymobCashInAuthenticator(...)
    {
        // ...
        _optionsChangeSubscription = options.OnChange(_ =>
        {
            _cachedToken = null;
            _tokenExpiration = DateTimeOffset.MinValue;
        });
    }

    public void Dispose()
    {
        _optionsChangeSubscription?.Dispose();
        _tokenLock.Dispose();
    }
}
```

**Pros:**
- Proper resource cleanup
- Follows .NET patterns
- Works well with DI container disposal

**Cons:**
- Requires IDisposable implementation

**Effort:** 30 minutes

**Risk:** Low

## Recommended Action

**To be filled during triage.**

## Technical Details

**Affected files:**
- `src/Framework.Payments.Paymob.CashIn/PaymobCashInAuthenticator.cs`

## Acceptance Criteria

- [ ] IOptionsMonitor subscription stored in field
- [ ] IDisposable implemented
- [ ] Dispose method cleans up subscription and SemaphoreSlim

## Work Log

### 2026-01-11 - Initial Discovery

**By:** Claude Code (Code Review)

**Actions:**
- Identified missing disposal of OnChange subscription
- Drafted IDisposable implementation
