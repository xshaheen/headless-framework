---
domain: Context
packages: Context.Abstractions, Context
---

# Context

> Ambient execution context: who is calling, in which locale and time zone, how operations correlate and cancel, and which host the process is.

## Orientation

- **`Headless.Context.Abstractions`**: the contracts and their dependency-free defaults, all in namespace `Headless.Context`: `ICurrentUser` (`NullCurrentUser`), `ICurrentPrincipalAccessor`, `PrincipalContext`, `ICurrentLocale` (`DefaultCurrentLocale`, `CurrentCultureCurrentLocale`), `ICurrentTimeZone` (`LocalCurrentTimeZone`), `ICorrelationIdProvider`, `ICancellationTokenProvider` (`DefaultCancellationTokenProvider`), `IHostIdentityAccessor` with `HostIdentityOptions`, and `IBuildInformationAccessor`.
- **`Headless.Context`**: implementations that need DI or logging: `PrincipalCurrentUser`, `CurrentPrincipalAccessor`, `ThreadCurrentPrincipalAccessor`, `ActivityCorrelationIdProvider`, `BuildInformationAccessor`, `HostIdentityAccessor`, and `AddHeadlessHostIdentity()`.
- **Who registers them.** In an ASP.NET Core host, `AddHeadless()` from `Headless.Api.ServiceDefaults` registers HTTP-backed `ICurrentUser`, `ICurrentPrincipalAccessor`, and `ICancellationTokenProvider` (the request's abort token), plus `CurrentCultureCurrentLocale`, `LocalCurrentTimeZone`, and host identity ([API](api.md#headlessapiservicedefaults)). `Headless.EntityFramework` and the relational audit log register `NullCurrentUser` and `ActivityCorrelationIdProvider` when nothing else did. A worker host without HTTP registers its own `ICurrentUser` and `ICurrentPrincipalAccessor`.
- **Documented elsewhere.** `LogState`, `HeadlessLoggerExtensions`, and `AddHeadlessGuidGenerator()` ship in `Headless.Hosting` ([utilities.md](utilities.md#headlesshosting)). `IGuidGenerator`, `SequentialGuidType`, and `IPasswordGenerator` ship in `Headless.Extensions` ([extensions.md](extensions.md#headlessextensions)). `ITimezoneProvider` and `IEnumLocaleAccessor` ship in `Headless.Api.Abstractions` ([api.md](api.md)). `ICurrentTenant` ships in `Headless.MultiTenancy.Abstractions` ([multi-tenancy.md](multi-tenancy.md)).

## Agent Rules

- Inject `ICurrentUser` for the caller's identity and claims, and `ICurrentTenant` for the tenant. In unit tests, use `TestCurrentUser` and `TestCurrentTenant` from `Headless.Testing` ([testing.md](testing.md#headlesstesting)).
- To act as another principal for a bounded piece of work, call `using var scope = accessor.Change(principal);` on `ICurrentPrincipalAccessor`. Disposing the scope restores the previous principal.
- Inject the BCL `TimeProvider` for every time you read; the framework has no clock abstraction of its own. Do not call `DateTime.Now`, `DateTime.UtcNow`, `DateTimeOffset.Now`, or `DateTimeOffset.UtcNow`: tests cannot control them.
- Pick the clock by the question the timestamp answers:

  | Question | Examples | Clock |
  | --- | --- | --- |
  | Who owns this, and until when? | Leases, locks, liveness, visibility | The store's clock, inside the atomic statement. Pass a duration; never compute the deadline from the app clock. |
  | How long has this taken? | Timeouts, back-off, deadlines | A monotonic clock: `TimeProvider.GetTimestamp()` and `GetElapsedTime()`. |
  | When should this fire in human terms? | Cron, calendars | The tz database through an explicit `TimeZoneInfo`, never `TimeZoneInfo.Local`. |
  | When did this happen? | Audit fields, `CreatedAt`, logs | The injected `TimeProvider`: `timeProvider.GetUtcNow()`. |

  Only the last row reads the app clock, so only that row moves when a test advances a `FakeTimeProvider`.
- Use `DateTimeOffset` for persisted instants and public timestamps.
- Register GUID generation with `AddHeadlessGuidGenerator()` in host setup. Code that writes database keys resolves the keyed `SequentialGuidType.Version7` or `SequentialGuidType.SqlServer` generator, not the unkeyed default ([extensions.md](extensions.md#guid-ordering-strategy)).

---

## Headless.Context.Abstractions

Contracts and dependency-free defaults for the ambient execution context.

### Setup

```bash
dotnet add package Headless.Context.Abstractions
```

```csharp
using Headless.Context;
using Headless.MultiTenancy;

public sealed class OrderService(TimeProvider timeProvider, ICurrentUser user, ICurrentTenant tenant)
{
    public Order CreateOrder(decimal total)
    {
        return new Order
        {
            UserId = user.UserId!,
            TenantId = tenant.Id,
            CreatedAt = timeProvider.GetUtcNow(), // "When did this happen?": the app clock
            Total = total,
        };
    }
}
```

**Verify:** no startup signal; the package registers nothing. A missing registration fails when the consuming service is first resolved, with the container's "Unable to resolve service" `InvalidOperationException`. In a unit test, construct the service with `TestCurrentUser` and a `FakeTimeProvider` and assert the stamped values.

### API

| Contract | Behavior |
| --- | --- |
| `ICurrentUser` | `Principal`, `IsAuthenticated`, `UserId`, `AccountId`, `AccountType`, `Roles`, `FindClaim`, `FindClaims`. `UserId` and `Roles` are populated only for an authenticated principal. `NullCurrentUser` is the anonymous and background default: not authenticated, no roles. |
| `ICurrentPrincipalAccessor` | `Principal` is the ambient `ClaimsPrincipal`, or `null`. `Change(principal)` overrides it until the returned `IDisposable` is disposed. `Change(null)` removes the override and falls back to the implementation's default, such as `HttpContext.User` or `Thread.CurrentPrincipal`. Prefer `ICurrentUser` for reading. |
| `PrincipalContext` | A principal and tenant ID to resolve values for in place of the ambient ones. APIs that take it switch the ambient principal and tenant for the duration of the read, for example to resolve permissions for the principal a login response is about to issue. `TenantId` `null` means the host. |
| `ICurrentLocale` | `Language`, `Locale`, `LocaleCulture`. `DefaultCurrentLocale` is fixed `en` and `en-US`. `CurrentCultureCurrentLocale` follows `CultureInfo.CurrentCulture`. |
| `ICurrentTimeZone` | `TimeZone`. `LocalCurrentTimeZone` returns `TimeZoneInfo.Local`. |
| `ICorrelationIdProvider` | The correlation ID for tracing, audit rows, and logs. |
| `ICancellationTokenProvider` | `Token`, the ambient cancellation token. `DefaultCancellationTokenProvider.Instance` always returns `CancellationToken.None`. `provider.FallbackToProvider(token)` returns `token` when it is not `CancellationToken.None`, else the provider's token. An explicit token wins, and the provider's token is then not observed; the two are never linked. |
| `IHostIdentityAccessor` | `ApplicationName` and `HostName` of the running process. Coordination's node ID, the definition-store locks of Settings, Features, and Permissions, and change-announcement origins all read it, so one override in `HostIdentityOptions` moves them all. It has no per-start instance ID; Coordination allocates that. |
| `IBuildInformationAccessor` | `GetTitle()`, `GetProduct()`, `GetDescription()`, `GetCompany()`, `GetVersion()`, `GetCommitNumber()` from the entry assembly. |

### Configuration

None.

### Design and runtime behavior

The package registers nothing. `TimeProvider` needs no wiring in most hosts, because Headless setup extensions register `TimeProvider.System` when no `TimeProvider` is registered. In tests, replace it with `FakeTimeProvider` ([testing.md](testing.md#controllable-time)).

## Headless.Context

Implementations and DI setup for principal access, correlation, build information, and host identity.

### Setup

```bash
dotnet add package Headless.Context
```

```csharp
using Headless.Context;

var builder = WebApplication.CreateBuilder(args);

// Every registration is TryAdd: the first call wins, so make the host's call before feature packages run theirs.
builder.Services.AddHeadlessHostIdentity(options => options.HostName = "orders-worker-0");
```

**Verify:** resolve `IHostIdentityAccessor` and assert `HostName` is `orders-worker-0`. An empty or white-space `ApplicationName` or `HostName` throws `ArgumentException` when the accessor is first resolved. A host that cannot discover any name logs the `GeneratedFallbackHostName` warning.

### API

- `PrincipalCurrentUser`: `ICurrentUser` over an explicit `ClaimsPrincipal`, such as `HttpContext.User`.
- `CurrentPrincipalAccessor`: a base class that keeps an `AsyncLocal` override over a fallback principal you define. `ThreadCurrentPrincipalAccessor` falls back to `Thread.CurrentPrincipal`; use it in console apps and worker services.
- `ActivityCorrelationIdProvider`: returns the W3C trace ID of `Activity.Current` as 32 lowercase hex characters. Every span of one request, and every audit row and log written under it, shares that ID, and it matches the trace in your tracing backend. It returns `null` when no activity is current or the activity uses the hierarchical ID format. OpenTelemetry users get it with no extra setup.
- `BuildInformationAccessor`: reads the entry assembly's metadata. Every member returns `null` when there is no managed entry assembly.
- `AddHeadlessHostIdentity(configure)`: registers `IHostIdentityAccessor`, `IBuildInformationAccessor`, and `AddHeadlessGuidGenerator()` (the keyed `Version7` and `SqlServer` generators and the unkeyed default).

### Configuration

| `HostIdentityOptions` | Default when unset |
| --- | --- |
| `ApplicationName` | The entry assembly's title, else `Unknown`. |
| `HostName` | `POD_NAMESPACE/POD_NAME` when `POD_NAME` is set (`POD_NAME` alone when `POD_NAMESPACE` is not), else the machine name, else `generated-<guid>` with a `GeneratedFallbackHostName` warning. |

Set `HostName` when the deployment has a stable identity the defaults cannot see. A generated name changes on every start, so cluster membership treats each start as a new host.

### Design and runtime behavior

- `AddHeadlessHostIdentity()` uses `TryAdd` for every registration. The first call's options win; a later call, from the host or a feature package, changes nothing.
- `HostName` stays stable across container restarts in one Kubernetes pod, so a store recognizes a returning node.
- For retries, reference `Polly.Core` and build a `ResiliencePipeline` directly; the Headless foundation packages do not bring Polly in. A retry back-off waits real time, so leave the pipeline on its default clock rather than the app's `TimeProvider`:

  ```csharp
  using Polly;
  using Polly.Retry;

  var pipeline = new ResiliencePipelineBuilder()
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

  var response = await pipeline.ExecuteAsync(async ct => await httpClient.GetAsync(url, ct), cancellationToken);
  ```
