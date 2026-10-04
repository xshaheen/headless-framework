---
domain: Context
packages: Context.Abstractions, Context
---

# Context

> Ambient execution context — who is calling, in which locale and time zone, how operations correlate and cancel, and which host the process is — for the Headless framework.

## Orientation

- **`Headless.Context.Abstractions`** — the ambient-context contracts, dependency-free: `ICurrentUser` (+ `NullCurrentUser`), `ICurrentPrincipalAccessor`, `PrincipalContext`, `ICurrentLocale` (+ `DefaultCurrentLocale`, `CurrentCultureCurrentLocale`), `ICurrentTimeZone` (+ `LocalCurrentTimeZone`), `ICorrelationIdProvider`, `ICancellationTokenProvider` (+ `DefaultCancellationTokenProvider`), `IHostIdentityAccessor` / `HostIdentityOptions`, and `IBuildInformationAccessor`. Namespace: `Headless.Context`.
- **`Headless.Context`** — implementations and DI setup that need DI/logging: `PrincipalCurrentUser`, `CurrentPrincipalAccessor` / `ThreadCurrentPrincipalAccessor`, `ActivityCorrelationIdProvider`, `BuildInformationAccessor`, `HostIdentityAccessor`, and `AddHeadlessHostIdentity()`. Namespace: `Headless.Context`.
- **Related, documented elsewhere**: `LogState` / `HeadlessLoggerExtensions` and `AddHeadlessGuidGenerator()` ship in `Headless.Hosting` (see [utilities.md](utilities.md)); `IPasswordGenerator` / `PasswordGenerator` in the Security family (see [security.md](security.md)); `ITimezoneProvider` / `IEnumLocaleAccessor` in `Headless.Api.Abstractions` (see [api.md](api.md)). The tenant-context surface (`ICurrentTenant` and friends) lives in `Headless.MultiTenancy.Abstractions` — see [multi-tenancy.md](multi-tenancy.md).

## Agent Rules

- Inject `ICurrentUser` for the caller's identity and claims, `ICurrentTenant` for the tenant. Both have test doubles in `Headless.Testing` (`TestCurrentUser`, `TestCurrentTenant`).
- Use `ICurrentPrincipalAccessor.Change(principal)` to switch the ambient principal for a bounded scope (return value is `IDisposable` — prefer a `using` declaration); the scope restores the previous principal on dispose.
- For time, use the BCL `TimeProvider` — the framework has no clock abstraction of its own. `DateTime.Now` / `DateTime.UtcNow` / `DateTimeOffset.Now` / `DateTimeOffset.UtcNow` are banned at compile time by the Headless SDK (`RS0030`).
- Name framework-owned event timestamps with an `At` suffix (`CreatedAt`, `UpdatedAt`, `DeletedAt`, `PublishedAt`) and use an `On` suffix only for `DateOnly` values (`EffectiveOn`). Avoid `DateCreated`-style prefixes; persisted instants and public timestamp contracts use `DateTimeOffset`.
- Time semantics belong to whichever authority owns the decision, not to the ambient environment of the running process. Pick by the question the timestamp answers: **"who owns this, and until when?"** (leases, locks, liveness, visibility) → the **store's** clock, inlined into the atomic statement; **"how long has this taken?"** (timeouts, backoff, deadlines) → a **monotonic** clock, `TimeProvider.GetTimestamp()` / `GetElapsedTime()`; **"when should this fire in human terms?"** (cron, calendars) → the **tz database** via an explicit `TimeZoneInfo` (never `TimeZoneInfo.Local`); **"when did this happen?"** (audit, `CreatedAt`, logs) → the injected **`TimeProvider`** (`timeProvider.GetUtcNow()`). Full rationale: [temporal-authority-standard](../solutions/design-patterns/temporal-authority-standard.md).
- Only that last row is an app-clock concern. Never sample the app clock to compute a lease deadline — pass a duration and let the store apply its own clock.
- Register GUID generation through `AddHeadlessGuidGenerator()` (in `Headless.Hosting`) only from host/package setup; persisted backends should resolve `SequentialGuidType.Version7` or `SequentialGuidType.SqlServer` by key instead of depending on the unkeyed default. The `IGuidGenerator` / `SequentialGuidType` contracts live in `Headless.Extensions` (see [extensions.md](extensions.md)).
- Use `LogState` with `HeadlessLoggerExtensions` (both in `Headless.Hosting`) for structured logging with tags and properties.

---

## Headless.Context.Abstractions

Contracts and dependency-free defaults for the ambient execution context.

### API and behavior

- `ICurrentUser` / `NullCurrentUser` — current authenticated user context; `UserId` and `Roles` are exposed only for authenticated principals. `NullCurrentUser` is the anonymous/background default.
- `ICorrelationIdProvider` — correlation ID for tracing, audit, and structured logging; the `Activity.Current`-backed `ActivityCorrelationIdProvider` ships in `Headless.Context`.
- `ICurrentLocale` (+ `DefaultCurrentLocale`, `CurrentCultureCurrentLocale`) — localization context (language, locale, culture).
- `ICurrentTimeZone` (+ `LocalCurrentTimeZone`) — the time zone treated as current for the ambient scope.
- `ICurrentPrincipalAccessor` — scoped `ClaimsPrincipal` access with temporary switching; the base `CurrentPrincipalAccessor` and the thread fallback `ThreadCurrentPrincipalAccessor` ship in `Headless.Context`.
- `ICancellationTokenProvider` (+ `DefaultCancellationTokenProvider`, `FallbackToProvider`) — ambient cancellation. Override semantics: an explicitly supplied token wins and the provider's token is not observed.
- `IHostIdentityAccessor` / `HostIdentityOptions` — process identity for every subsystem that stamps an origin on shared state. `HostName` is resolved in order from `HostIdentityOptions.HostName`, `POD_NAMESPACE`/`POD_NAME`, and the machine name. There is no per-start instance id here on purpose: Coordination allocates the only one (`NodeIdentity`, `host@incarnation`) on top of this host name, so nothing can disagree with it. Coordination's node id, the settings/features/permissions definition-store locks, and the change-announcement origin all read from the accessor, so an override in `HostIdentityOptions` moves every subsystem at once.
- `IBuildInformationAccessor` — build-time metadata (title, product, version, commit) read from the entry assembly; the default `BuildInformationAccessor` ships in `Headless.Context`.

### Install

```bash
dotnet add package Headless.Context.Abstractions
```

### Setup and use

```csharp
public sealed class OrderService(TimeProvider timeProvider, ICurrentUser user, ICurrentTenant tenant)
{
    public Order CreateOrder(CreateOrderRequest request)
    {
        return new Order
        {
            Id = Guid.NewGuid(),
            UserId = user.UserId!,
            TenantId = tenant.Id,
            // "When did this happen?" — an audit timestamp, so the injected app clock owns it.
            CreatedAt = timeProvider.GetUtcNow(),
            Total = new Money(request.Amount, request.Currency),
        };
    }
}
```

`TimeProvider` is registered as a singleton by the Headless setup extensions (`TryAddSingleton(TimeProvider.System)`), so it is injectable without extra wiring. In tests, swap it for `FakeTimeProvider` — see [testing.md](testing.md).

### Configuration

None. These are contracts with dependency-free defaults.

### Runtime behavior

None. Nothing is registered by this package.

## Headless.Context

Implementations and DI setup for the ambient context: principal access, correlation, build information, and host identity.

### API and behavior

- `PrincipalCurrentUser` — `ICurrentUser` over an explicit `ClaimsPrincipal` (for example `HttpContext.User`).
- `CurrentPrincipalAccessor` — base class layering an `AsyncLocal` override slot over an implementation-defined fallback; `ThreadCurrentPrincipalAccessor` uses `Thread.CurrentPrincipal` (console apps, worker services).
- `ActivityCorrelationIdProvider` — reads the correlation ID from `Activity.Current`; works automatically for OpenTelemetry users, returns `null` when no activity is active.
- `BuildInformationAccessor` — reads build metadata from `AssemblyInformation.Entry`; all members return `null` when there is no managed entry assembly.
- `HostIdentityAccessor` + `AddHeadlessHostIdentity()` — registers `IHostIdentityAccessor` with its build-information and GUID dependencies. Every registration is `TryAdd`, so feature packages call this for the identity they need and the host's own call, or an earlier package's, wins. `ApplicationName` defaults to the entry assembly title and `HostName` to `POD_NAMESPACE/POD_NAME`, then the machine name.

### Install

```bash
dotnet add package Headless.Context
```

### Setup and use

```csharp
var builder = WebApplication.CreateBuilder(args);

// TryAdd: a feature package's earlier call keeps the host's choice
builder.Services.AddHeadlessHostIdentity(options => options.HostName = "orders-worker-0");
```

For retries and delayed execution, reference the `Polly.Core` package and use it directly — the Headless foundation packages do not bring it in, and it ships zero transitive dependencies on `net10.0`:

```csharp
using Polly;
using Polly.Retry;

private static readonly ResiliencePipeline _RetryPipeline = new ResiliencePipelineBuilder()
    .AddRetry(
        new RetryStrategyOptions
        {
            ShouldHandle = new PredicateBuilder().Handle<HttpRequestException>(),
            MaxRetryAttempts = 3,
            BackoffType = DelayBackoffType.Exponential,
            Delay = TimeSpan.FromMilliseconds(100),
            MaxDelay = TimeSpan.FromSeconds(1),
            UseJitter = true,
        }
    )
    .Build();

var result = await _RetryPipeline.ExecuteAsync(
    async ct => await httpClient.GetAsync(url, ct).ConfigureAwait(false),
    cancellationToken
);
```

### Configuration

`HostIdentityOptions.ApplicationName` / `.HostName` — optional overrides; every unset member is discovered.

### Runtime behavior

- `AddHeadlessHostIdentity()` also registers `AddHeadlessGuidGenerator()` (keyed `Version7` / `SqlServer` strategies plus the unkeyed default) and `TryAddSingleton<IBuildInformationAccessor, BuildInformationAccessor>()`.
- The structured-logging helpers `LogState` / `HeadlessLoggerExtensions` and `AddHeadlessGuidGenerator()` ship in `Headless.Hosting` (see [utilities.md](utilities.md)).
