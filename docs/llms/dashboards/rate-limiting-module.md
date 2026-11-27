---
domain: Dashboards
packages:
---

# Rate limits module

> Part of [Dashboards](../dashboards.md). The Rate limits module from `Headless.RateLimiting.Dashboard`: registration, options, the lookup and catalogue endpoints, the Reset command, and the rules that keep the subject out of URLs and logs.

## Register the module

```bash
dotnet add package Headless.RateLimiting.Dashboard
```

`AddRateLimiting()` adds the Rate limits module under the id `rate-limiting`, in the Platform product, with a Rate limits sidebar entry whose count is the number of declared purposes. It needs `AddAttemptLimiter(...)` in the same host: a host without it fails to start with a message naming that call.

```csharp
using Headless.Caching;
using Headless.Dashboard;
using Headless.RateLimiting;

builder.Services.AddHeadlessCaching(setup => setup.UseInMemory());
builder.Services.AddAttemptLimiter(options =>
{
    options.SubjectKey = builder.Configuration["Attempts:SubjectKey"]!;
    options.Purposes["sign-in.password"] = new AttemptQuota(5, TimeSpan.FromMinutes(15));
    options.Purposes["otp.verify"] = new AttemptQuota(3, TimeSpan.FromMinutes(10));
});
builder.Services.AddHeadlessDashboard(dashboard => dashboard
    .WithHostAuthentication(host => { host.ViewPolicy = "ops.view"; host.OperatePolicy = "ops.operate"; })
    .AddRateLimiting());

var app = builder.Build();
app.MapHeadlessDashboard();
```

**Verify:** a signed-in viewer's `GET {base}/api/rate-limiting/catalogue` (by default `/headless/api/rate-limiting/catalogue`) answers `200` and lists every purpose in `AttemptLimiterOptions.Purposes`, and the Platform tab's sidebar shows Rate limits with that count.

| `RateLimitingDashboardOptions` member | Default | Description |
| --- | --- | --- |
| `ViewPolicy`, `OperatePolicy` | the shell's | As for every module. A white-space name fails options validation at startup. |

`AddRateLimiting(Action<RateLimitingDashboardOptions>)`, `AddRateLimiting(Action<RateLimitingDashboardOptions, IServiceProvider>)`, and `AddRateLimiting(IConfiguration)` configure them.

Declare every flow the app limits in `AttemptLimiterOptions.Purposes`. The screen lists only declared purposes, and a lookup or a reset of a declared purpose always uses its declared quota. An app that acquires a purpose with another window counts in another counter, so the screen reads zero for it. A custom `IAttemptLimiter` works only when `AddAttemptLimiter` is also called, because that call registers the `AttemptSubjectFingerprinter` the module derives fingerprints from; under a custom limiter the catalogue leaves the counter store out.

## API

| Route | Policy | Answers |
| --- | --- | --- |
| `GET {base}/api/rate-limiting/catalogue` | view | `{ purposes: [{ name, limit, windowSeconds }], shortest, counterStore, cacheName }`, purposes sorted by name. `counterStore` is `named` (with `cacheName`), `remote`, or `default`, the cache the built-in limiter counts in; it is absent under a custom limiter. |
| `POST {base}/api/rate-limiting/lookup` | view | One subject's current window: `{ purpose, fingerprint, count, limit, remaining, isLockedOut, windowStartsAt, windowEndsAt, closesInSeconds, quota, declared }`. |

The lookup body is `{ purpose, subject, quota? }` with `quota = { limit, windowSeconds }`. It is a POST so the subject never reaches a URL or a request log; send `X-Requested-With` as for every dashboard POST.

- A lookup spends no attempt and creates no counter. A subject with no attempts reads `count: 0`.
- `count` includes refused attempts and can exceed `limit`. `closesInSeconds` is read at lookup time and is at least 1.
- `fingerprint` is `AttemptSubjectFingerprinter.Compute(purpose, subject)`. The answer never carries the subject.
- An undeclared purpose needs `quota`; a declared one takes no quota or exactly the declared one. Anything else answers `400` on `quota`.
- A blank or longer-than-512-character `subject`, a blank `purpose`, or malformed JSON answers `400` naming the field. No problem carries the subject's value.
- A failing counter store answers `503` whose `detail` is `The attempt limiter for '<purpose>' is unavailable.`, never a zero count.
- The module types the subject as given. Normalise it the way the app does before it calls the limiter (lower-case an email, E.164 a phone number), or the lookup reads another counter.

## Commands

| Command | On | Allowed when |
| --- | --- | --- |
| `rate-limiting.reset` | `rate-limit-subject` | the user may operate; it needs a confirmation and a reason |

The target id is `{purpose}:{fingerprint}`, and the subject is the `subject` argument (text, required, at most 512 characters). An undeclared purpose also takes `limit` and `windowSeconds` (whole numbers, at least 1). One call resets one target. The command removes only the current window's counter: protection is lifted until the window would have closed, and the next window starts from zero as usual.

| Outcome | When | Operator log |
| --- | --- | --- |
| `done` | the window had attempts and its counter was removed | one entry, `Done = 1` |
| `skipped`, `No attempts in the current window` | the window had no attempts | one entry, `Skipped = 1` |
| `failed`, `The attempt limiter for '<purpose>' is unavailable.` | the counter store failed | one entry, `Failed = 1` |
| `409` refusal | the subject does not hash to the target's fingerprint, the reason contains the subject (any case), the quota differs from the declared one, or more than one target | none; the counter is unchanged |

The shell writes the target id and the reason to the operator log as they are and never writes arguments, so the log names the subject only by its fingerprint. That is why a reason that repeats the subject is refused: write `Verified by phone`, not the address.

A viewer's `GET {base}/api/rate-limiting/commands/allowed?kind=rate-limit-subject&id=…` answers Reset as not allowed with the shell's operate-permission reason, and running it answers `403`. The allowed-actions answer cannot know whether the subject has attempts, because that needs the subject; the screen disables Reset with `No attempts in the current window` from its lookup instead.

## The screen

Rate limits lists the declared purposes with their limit and window, states how many are declared and the shortest window, and shows the counter store when the catalogue serves it. A lookup shows Locked out or Within limit, the count against the limit, the remaining attempts, the window's start and end, the countdown as read with its time, and the fingerprint. Once the window ends, a note replaces the countdown and Refresh reads the current one. Editing the purpose, the quota, or the subject clears the result, so Reset acts only on the subject the result shows. The looked-up subject has no URL by design.

Counts cannot be browsed: the cache has no enumeration, so the screen looks up one subject at a time. Through a hybrid cache a lookup can read a stale local copy and a reset can report done before every instance sees it; point the limiter at the remote cache (`AttemptLimiterOptions.CacheName`, or a registered remote tier) before you rely on the screen.
