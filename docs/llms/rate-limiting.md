---
domain: Rate Limiting
packages: RateLimiting
---

# Rate Limiting

> Exact, cross-process attempt quotas for flows an attacker can drive for free, counted in `ICache` under pseudonymised keys.

## Orientation

Use `IAttemptLimiter` from `Headless.RateLimiting` to cap how often one subject may do one thing: send a verification code to a phone number, request a password reset for an email address, submit a PIN for a card. The subject is usually known before any account exists, which is why these budgets cannot hang off a user id.

The limiter counts in fixed windows over `ICache.IncrementAsync`, so every replica that shares a Redis or hybrid cache shares one budget, and a restart does not hand anyone a fresh allowance.

For coarse per-request throughput shedding at the HTTP edge, use `Microsoft.AspNetCore.RateLimiting`. Its limiters are in-process, so their limits apply per replica. That is acceptable for load shedding and wrong for quotas that guard a secret; put those behind `IAttemptLimiter` in the handler.

## Agent Rules

- Register a shared cache provider (`UseRedis` or `UseHybrid`) with the limiter. `UseInMemory` counts per process, so N replicas admit N times the limit.
- Normalize the subject before calling `AcquireAsync`: lower-case emails, E.164 phone numbers, a canonical IP form. `"A@x.com"` and `"a@x.com"` are separate budgets.
- Keep personal data out of the purpose string. The purpose appears in the cache key in the clear; only the subject is pseudonymised.
- Acquire before the account lookup, so the response does not reveal whether an identifier exists.
- Call `ResetAsync` only after the guarded state change is committed, such as after a verification code is consumed. Resetting earlier lets a caller replay a half-finished verification without limit.
- Do not catch and ignore cache exceptions from `AcquireAsync`. They propagate so the guarded operation fails closed.
- Pass the quota on every call. Read it from options or settings each time rather than caching an `AttemptQuota` for the process lifetime; a changed limit then applies on the next attempt.
- Load `SubjectKey` from a secret store. Rotating it starts every budget over.

## Core Concepts

### Purpose, subject, and quota

A **purpose** names the protected flow (`"otp-delivery"`, `"password-reset:email"`). Purposes keep separate counters even when they share a quota, so spending one flow's budget never locks the caller out of another.

A **subject** is whoever the budget belongs to: a phone number, email address, IP address, card id, or account id.

An **`AttemptQuota(limit, window)`** admits at most `limit` attempts per `window`. The window is a positive whole number of seconds.

### Windows

Windows are aligned to absolute Unix time, not to first use: a one-minute window runs from `:00` to `:59` on every replica without coordination. The window number is part of the cache key, so the window closes when the clock moves past it, whatever the store does with the key's TTL. Each counter expires when its window closes.

Replicas read their own clocks. Right at a window boundary, a replica whose clock lags can still charge the closing window, so a caller who times requests to the boundary can get up to one extra window's allowance. Keep host clocks synchronized.

### Every attempt is charged

`AcquireAsync` increments before it compares, and it counts refused attempts too. A caller who keeps guessing past the limit does not earn allowance back by failing, and concurrent attempts across replicas cannot both take the last permit.

## Headless.RateLimiting

Fixed-window attempt quotas over `ICache`.

### Setup

```bash
dotnet add package Headless.RateLimiting
```

```csharp
builder.Services.AddHeadlessCaching(setup => setup.UseRedis(builder.Configuration.GetSection("Redis")));
builder.Services.AddAttemptLimiter(builder.Configuration.GetSection("AttemptLimiter"));
```

`AddAttemptLimiter` also accepts `Action<AttemptLimiterOptions>` and `Action<AttemptLimiterOptions, IServiceProvider>`. Host startup fails when no `ICache` is registered.

```csharp
public sealed class SendLoginCodeHandler(IAttemptLimiter limiter, ISmsSender sms)
{
    private static readonly AttemptQuota _Quota = new(limit: 5, window: TimeSpan.FromMinutes(15));

    public async Task HandleAsync(string phoneNumber, CancellationToken cancellationToken)
    {
        var attempt = await limiter.AcquireAsync("login-code-delivery", phoneNumber, _Quota, cancellationToken);

        // Throws TooManyRequestsException; the API exception handler turns it into 429 with Retry-After.
        attempt.ThrowIfRejected(new ErrorDescriptor("login_code_attempts_exceeded", "Too many codes requested."));

        // ... send the code
    }
}
```

For a verification flow, keep the result and reset once the code is consumed:

```csharp
var attempt = await limiter.AcquireAsync("pin-verify", cardId, quota, cancellationToken);
attempt.ThrowIfRejected();

// ... verify the PIN and commit

await limiter.ResetAsync(attempt, cancellationToken);
```

### Configuration

| Option | Default | Meaning |
| --- | --- | --- |
| `SubjectKey` | required | Base64 secret of at least 32 bytes. Subjects are HMAC-SHA256'd with it before they reach a cache key. |
| `KeyPrefix` | `attempts` | First segment of every counter key. Change it when applications share one cache. |

### Design and runtime behavior

- `AttemptResult` reports `IsAllowed`, `Count` (attempts charged in the window, including refused ones), `Limit`, `Remaining`, and `RetryAfter`. `RetryAfter` is the time until the window closes, at least one second. A caller needs it only once `Remaining` reaches zero.
- `ThrowIfRejected(error)` throws `Headless.Exceptions.TooManyRequestsException`. With `Headless.Api` problem details registered, that maps to 429, the `Retry-After` header in whole seconds rounded up, and `retryAfter` plus the optional `error` in the body. Outside HTTP, catch it or read `IsAllowed` instead.
- Lowering a quota's `Limit` applies to the running window on the next attempt. Changing its `Window` starts every subject on a fresh counter, because the window length is part of the key.
- Cache keys have the shape `{KeyPrefix}:v1:{purpose}:{windowSeconds}:{windowNumber}:{fingerprint}`. The fingerprint is a base64url HMAC of the purpose and subject, so the same subject yields unrelated fingerprints under different purposes.
- A cache failure in `AcquireAsync` or `ResetAsync` propagates.
- `AttemptResult` is publicly constructible, so a test can stub `IAttemptLimiter` without a cache: `new AttemptResult("otp-delivery", count: 6, limit: 5, retryAfter: TimeSpan.FromSeconds(30))` is a refusal. `ResetToken` is the opaque handle `ResetAsync` needs; a hand-built result has none, and the built-in limiter's `ResetAsync` throws `ArgumentException` for it. A custom `IAttemptLimiter` puts its own reset state in the token.
