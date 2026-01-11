---
status: pending
priority: p1
issue_id: "136"
tags: [code-review, threading, paymob, cashin]
dependencies: []
---

# Race Condition in Token Caching

## Problem Statement

The `PaymobCashInAuthenticator` has a classic check-then-act race condition. Multiple concurrent requests when the token expires will all call the Paymob API simultaneously, wasting resources and potentially triggering rate limiting.

Additionally, `_cachedToken` and `_tokenExpiration` are written non-atomically, allowing another thread to read inconsistent state.

## Findings

- **Location**: `src/Framework.Payments.Paymob.CashIn/PaymobCashInAuthenticator.cs:58-68`
- **Vulnerable code**:
  ```csharp
  public async ValueTask<string> GetAuthenticationTokenAsync()
  {
      if (_cachedToken is not null && _tokenExpiration > _timeProvider.GetUtcNow())
      {
          return _cachedToken;  // Multiple threads can pass this check
      }
      var response = await RequestAuthenticationTokenAsync();  // All call API
      return response.Token;
  }
  ```
- Singleton lifetime amplifies the issue (all scopes share one instance)
- 100 concurrent requests = 100 token requests instead of 1
- Non-atomic writes: `_cachedToken = content!.Token; _tokenExpiration = ...`

## Proposed Solutions

### Option 1: SemaphoreSlim with Double-Check Locking (Recommended)

**Approach:** Use async-safe mutual exclusion.

```csharp
private readonly SemaphoreSlim _tokenLock = new(1, 1);

public async ValueTask<string> GetAuthenticationTokenAsync(CancellationToken ct = default)
{
    // Fast path - no lock needed for reads
    if (_cachedToken is not null && _tokenExpiration > _timeProvider.GetUtcNow())
    {
        return _cachedToken;
    }

    await _tokenLock.WaitAsync(ct);
    try
    {
        // Double-check after acquiring lock
        if (_cachedToken is not null && _tokenExpiration > _timeProvider.GetUtcNow())
        {
            return _cachedToken;
        }

        var response = await RequestAuthenticationTokenAsync(ct);
        return response.Token;
    }
    finally
    {
        _tokenLock.Release();
    }
}
```

**Pros:**
- Prevents thundering herd problem
- Async-safe
- Well-understood pattern

**Cons:**
- Adds SemaphoreSlim allocation
- Requires IDisposable implementation

**Effort:** 1 hour

**Risk:** Low

---

### Option 2: AsyncLazy<T> Pattern

**Approach:** Use lazy initialization with async support.

**Pros:**
- Cleaner API
- Built-in thread safety

**Cons:**
- More complex for expiration handling
- May need custom implementation

**Effort:** 2 hours

**Risk:** Medium

## Recommended Action

**To be filled during triage.**

## Technical Details

**Affected files:**
- `src/Framework.Payments.Paymob.CashIn/PaymobCashInAuthenticator.cs:16-17, 36-56, 58-68`

**Related components:**
- All broker methods that call `authenticator.GetAuthenticationTokenAsync()`
- DI registration (Singleton lifetime)

## Resources

- **Double-check locking**: https://en.wikipedia.org/wiki/Double-checked_locking

## Acceptance Criteria

- [ ] SemaphoreSlim added for thread-safe token refresh
- [ ] Double-check locking pattern implemented
- [ ] IDisposable implemented to dispose SemaphoreSlim
- [ ] Unit tests for concurrent access scenarios
- [ ] Integration test for token expiration handling

## Work Log

### 2026-01-11 - Initial Discovery

**By:** Claude Code (Code Review)

**Actions:**
- Identified race condition in token caching
- Analyzed Singleton lifetime implications
- Drafted SemaphoreSlim solution

**Learnings:**
- Singleton services with mutable state need careful synchronization
- IOptionsMonitor.OnChange also needs thread-safe token invalidation
