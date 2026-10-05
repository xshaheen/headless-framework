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

The framework ships no edge limiter, because which endpoints get which limits and who counts as the caller are application policy. It ships the pieces every application would otherwise rewrite: the 429 response (`UseHeadlessProblemDetails`), an IP partition key that groups an IPv6 host's /64 (`GetIpAddressPartition`), and health endpoints that are never limited. [HTTP edge limits](#http-edge-limits) shows the wiring.

## Agent Rules

- Register a shared cache provider (`UseRedis` or `UseHybrid`) with the limiter. `UseInMemory` counts per process, so N replicas admit N times the limit.
- Normalize the subject before calling `AcquireAsync`: lower-case emails, E.164 phone numbers, a canonical IP form. `"A@x.com"` and `"a@x.com"` are separate budgets.
- Keep personal data out of the purpose string. The purpose appears in the cache key in the clear; only the subject is pseudonymised.
- Acquire before the account lookup, so the response does not reveal whether an identifier exists.
- Call `ResetAsync` only after the guarded state change is committed, such as after a verification code is consumed. Resetting earlier lets a caller replay a half-finished verification without limit.
- Do not catch and ignore cache exceptions from `AcquireAsync`. They propagate so the guarded operation fails closed.
- Pass the quota on every call. Read it from options or settings each time rather than caching an `AttemptQuota` for the process lifetime; a changed limit then applies on the next attempt.
- Load `SubjectKey` from a secret store. Rotating it starts every budget over.
- At the HTTP edge, call `options.UseHeadlessProblemDetails()` inside `AddRateLimiter`. Without it, a rejection is ASP.NET Core's default 503 with no `Retry-After` header and no `g:rate_limit_exceeded` code.
- Place `app.UseRateLimiter()` after `UseAuthentication()`, so a partition can read claims, and before `UseAuthorization()`, `UseHeadlessTenancy()`, and `UseHeadlessHttpIdempotency()`, so unauthenticated floods and idempotent replays are counted. When the app calls `UseRouting()` itself, put the limiter after it; partitions keyed by endpoint metadata need the selected endpoint.
- Partition anonymous callers with `HttpContext.GetIpAddressPartition()`, not `RemoteIpAddress` or `GetIpAddress()`. A raw IPv6 address gives one host a fresh budget per address in its /64.
- Map your own probe endpoints with `.DisableRateLimiting()`. `MapHeadlessEndpoints()` already does this for `/health` and `/alive`.
- Put the limit values in the partition key when limits can change at runtime. ASP.NET Core caches one limiter per key, so a key without them keeps enforcing the old limit.

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

## HTTP Edge Limits

Edge limits shed load per caller before a request reaches its handler. They use the limiters in `Microsoft.AspNetCore.RateLimiting` and the framework pieces in `Headless.Api` and `Headless.Api.ServiceDefaults`, with no extra package.

A global limiter that gives each API surface its own limit, partitioned by account and falling back to the client IP:

```csharp
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

builder.Services.Configure<EdgeLimitOptions>(builder.Configuration.GetSection("EdgeLimits"));
builder.Services.AddRateLimiter(options =>
{
    options.UseHeadlessProblemDetails();
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, EdgeLimitKey>(context =>
    {
        var limits = context.RequestServices.GetRequiredService<IOptionsMonitor<EdgeLimitOptions>>().CurrentValue;
        var surface = context.GetApiSurface()?.SurfaceName;

        if (surface is null || !limits.Surfaces.TryGetValue(surface, out var limit))
        {
            return RateLimitPartition.GetNoLimiter(default(EdgeLimitKey));
        }

        var caller = context.User.GetAccountId()?.ToString() ?? context.GetIpAddressPartition() ?? "unknown";

        // The limit values are part of the key: a changed limit moves callers to a new limiter on their next
        // request, and a reload that changes nothing keeps every budget.
        return RateLimitPartition.GetSlidingWindowLimiter(
            new EdgeLimitKey(surface, caller, limit.PermitLimit, limit.Window),
            static key => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = key.PermitLimit,
                Window = key.Window,
                SegmentsPerWindow = 6,
                QueueLimit = 0,
            }
        );
    });
});

var app = builder.Build();
app.UseHeadless();
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();
app.MapHeadlessEndpoints();

public sealed class EdgeLimitOptions
{
    public Dictionary<string, EdgeLimit> Surfaces { get; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class EdgeLimit
{
    public int PermitLimit { get; set; }

    public TimeSpan Window { get; set; }
}

public readonly record struct EdgeLimitKey(string Surface, string Caller, int PermitLimit, TimeSpan Window);
```

### Design and runtime behavior

- `UseHeadlessProblemDetails()` sets `RejectionStatusCode` to 429 and `OnRejected` to write `IProblemDetailsCreator.TooManyRequests` with `error.code` `g:rate_limit_exceeded` (`GeneralErrorCodes.RateLimitExceeded`), a `Retry-After` header in whole seconds rounded up (at least 1), and `Cache-Control: no-store`. The body's `retryAfter` matches the header. A named policy with its own `OnRejected` still overrides it for its endpoints.
- The retry delay comes from the rejected lease's `MetadataName.RetryAfter`. Fixed-window, sliding-window, and token-bucket limiters publish it. A limiter that publishes none, such as `ConcurrencyLimiter`, gets one second.
- `GetIpAddressPartition(ipv6PrefixLength = 64)` returns the whole address for IPv4 and the masked network for IPv6 (`2001:db8:1:2::/64`), or `null` with no remote address. IPv4-mapped IPv6 addresses (`::ffff:10.0.0.1`), which a dual-stack listener reports for IPv4 clients, stay IPv4; masking them would put every IPv4 client in one `::/64` partition. Pass a shorter prefix, such as 56 or 48, for networks that assign each site a larger block.
- The partition is only as trustworthy as `UseForwardedHeaders`. Behind a proxy, configure `KnownProxies` or `KnownIPNetworks`; with `TrustForwardedHeadersFromAnyProxy`, any client can forge `X-Forwarded-For` and choose its own partition.
- `MapHeadlessEndpoints()` adds `DisableRateLimiting` metadata to `/health` and `/alive`, which skips both the global limiter and endpoint policies. Probes arrive from a few orchestrator addresses, so an IP-partitioned limiter would eventually reject one and take a healthy instance out of rotation.
- Endpoint policies (`[EnableRateLimiting("login")]`, `RequireRateLimiting("login")`) run after the global limiter, so an endpoint can be stricter than its surface.
- Limits apply per replica: N replicas admit N times the configured rate. Do not divide a limit by the replica count, because the count changes under autoscaling. A quota that must hold across replicas goes through `IAttemptLimiter`.
- Do not send `RateLimit-*` or `X-RateLimit-*` headers from these limiters. Their counts are per replica, so a client would read a budget that the next replica does not share.
- ASP.NET Core removes a partition's limiter once it has been idle for about 10 seconds, so the keys left behind by a changed limit, and the keys of callers who have gone quiet, do not accumulate.

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
- `ThrowIfRejected(error)` throws `Headless.TooManyRequestsException`. With `Headless.Api` problem details registered, that maps to 429, the `Retry-After` header in whole seconds rounded up, and `retryAfter` plus the optional `error` in the body. Outside HTTP, catch it or read `IsAllowed` instead.
- Lowering a quota's `Limit` applies to the running window on the next attempt. Changing its `Window` starts every subject on a fresh counter, because the window length is part of the key.
- Cache keys have the shape `{KeyPrefix}:v1:{purpose}:{windowSeconds}:{windowNumber}:{fingerprint}`. The fingerprint is a base64url HMAC of the purpose and subject, so the same subject yields unrelated fingerprints under different purposes.
- A cache failure in `AcquireAsync` or `ResetAsync` propagates.
- `AttemptResult` is publicly constructible, so a test can stub `IAttemptLimiter` without a cache: `new AttemptResult("otp-delivery", count: 6, limit: 5, retryAfter: TimeSpan.FromSeconds(30))` is a refusal. `ResetToken` is the opaque handle `ResetAsync` needs; a hand-built result has none, and the built-in limiter's `ResetAsync` throws `ArgumentException` for it. A custom `IAttemptLimiter` puts its own reset state in the token.
