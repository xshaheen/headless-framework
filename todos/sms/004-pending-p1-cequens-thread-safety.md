---
status: pending
priority: p1
issue_id: "004"
tags: [code-review, concurrency, cequens, sms, thread-safety]
dependencies: []
---

# CequensSmsSender token caching is not thread-safe

## Problem Statement

`CequensSmsSender` is registered as a singleton but has non-thread-safe token caching. Under concurrent load, multiple threads can simultaneously detect an expired token and make duplicate token requests, or experience torn reads/writes.

## Findings

- **File:** `src/Framework.Sms.Cequens/CequensSmsSender.cs:72-106`
- **Issues:**
  1. Check-then-act race condition on lines 77-79
  2. Non-atomic writes to `_cachedToken` and `_tokenExpiration` (lines 101-102)
  3. Multiple `DateTime.UtcNow` calls return different times
- **Registration:** Singleton in `Setup.cs:54`

```csharp
private string? _cachedToken;
private DateTime _tokenExpiration;

private async Task<string?> _GetTokenRequestAsync(CancellationToken cancellationToken)
{
    if (_cachedToken != null && _tokenExpiration > DateTime.UtcNow)  // Race!
    {
        return _cachedToken;
    }
    // Multiple threads can reach here simultaneously
    // ... fetch token ...
    _cachedToken = token;  // Non-atomic write
    _tokenExpiration = DateTime.UtcNow.AddMinutes(10);  // Different time!
}
```

## Proposed Solutions

### Option 1: SemaphoreSlim for async locking

**Approach:** Use `SemaphoreSlim` to ensure only one thread refreshes the token.

```csharp
private readonly SemaphoreSlim _tokenLock = new(1, 1);

private async Task<string?> _GetTokenRequestAsync(CancellationToken cancellationToken)
{
    var now = DateTime.UtcNow;
    if (_cachedToken != null && _tokenExpiration > now)
        return _cachedToken;

    await _tokenLock.WaitAsync(cancellationToken).AnyContext();
    try
    {
        // Double-check after acquiring lock
        now = DateTime.UtcNow;
        if (_cachedToken != null && _tokenExpiration > now)
            return _cachedToken;

        // ... fetch token ...
        _cachedToken = token;
        _tokenExpiration = now.AddMinutes(10);
        return token;
    }
    finally
    {
        _tokenLock.Release();
    }
}
```

**Pros:**
- Correct async-safe synchronization
- Prevents thundering herd
- Double-check pattern minimizes lock contention

**Cons:**
- Slightly more complex
- Need to dispose SemaphoreSlim

**Effort:** 1 hour

**Risk:** Low

---

### Option 2: Lazy<Task<T>> pattern

**Approach:** Use `Lazy<Task<T>>` with renewal on expiration.

**Pros:**
- Thread-safe by design

**Cons:**
- More complex renewal logic
- Less intuitive

**Effort:** 2 hours

**Risk:** Medium

## Recommended Action

Implement Option 1 (SemaphoreSlim) - it's the standard pattern for async-safe token caching.

## Technical Details

**Affected files:**
- `src/Framework.Sms.Cequens/CequensSmsSender.cs:72-106`

**Also fix:**
- Line 36: `httpClient.DefaultRequestHeaders.Authorization` mutation (use per-request headers)
- Capture `DateTime.UtcNow` once and reuse

## Acceptance Criteria

- [ ] Token caching is thread-safe under concurrent load
- [ ] Only one thread fetches token when expired
- [ ] DateTime captured once per operation
- [ ] SemaphoreSlim disposed properly (or class implements IDisposable)

## Work Log

### 2026-01-12 - Code Review Discovery

**By:** Claude Code

**Actions:**
- Identified race condition in token caching
- Confirmed singleton registration
- Proposed SemaphoreSlim-based solution
