# Headless.Extensions

The framework's base utility library: extension methods, domain primitives, value objects, the result pattern, and common helpers referenced by nearly every other `Headless.*` package.

## Problem Solved

Eliminates repetitive utility code — result/error modeling, strongly-typed domain values, parallel and keyed-async helpers, stream and file IO, reflection, validation, and a large catalog of constants — by providing one battle-tested, dependency-light base library so each downstream package does not re-implement the same primitives.

## Key Features

- **Result pattern** — `ApiResult` / `ApiResult<T>` with built-in error factories (`NotFound`, `Conflict`, `ValidationFailed`, `Forbidden`, `Unauthorized`); `Result<TValue, TError>` / `Result<TError>` for custom error types; the `ResultError` record hierarchy (`NotFoundError`, `UnauthorizedError`, `ForbiddenError`, `ConflictError`, `ValidationError`, `AggregateError`); `ErrorDescriptor` for structured, severity-tagged API errors; `Match`/`Map`/`Bind` combinators with `…Async` overloads.
- **Domain primitives** (source-generated, with JSON + TypeConverter support) — `UserId`, `AccountId`, `MoneyAmount` (decimal amount, banker's rounding), `Month` (1–12), `PhoneNumber` (libphonenumber-backed, digits-only canonicalization).
- **Value objects** (validated on construction) — `Money` (amount + currency code, scalar `*`/`/`, same-code `+`/`-`, total ordering), `GeoCoordinate` / `FullGeoCoordinate` (range-checked lat/long, Haversine distance), `Range<T>` (inclusive/exclusive containment, overlap), `PreferredLocale`, `TimeUnit` (duration-string parsing), `NameValue<T>`, `OrderBy`, `ExtraProperties` (ordinal-keyed property bag), `TenantInformation`, `PageMetadata`, `File` / `Image`.
- **Pagination** — `IndexPageRequest` / `IndexPage<T>` (offset) and `ContinuationPageRequest` / `ContinuationPage<T>` (cursor), each with `Select` / `Where` projection.
- **Collections** — `ParallelForEachAsync` (bounded concurrency) and `ForEachAsync` (ordered sequential, with index/cancellation overloads); `DetectChanges` (added/removed/updated/unchanged classification by key); `EquatableArray<T>` (value-equality array wrapper); `ComparerFactory`; `TypeList` / `ITypeList`; `HeadlessEnumerableExtensions` materialization helpers — `AsList` / `AsArray` / `AsICollection` / `AsIList` (→ `IList<T>`) / `AsIReadOnlyCollection` / `AsIReadOnlyList` (→ `IReadOnlyList<T>`) / `AsISet` / `AsHashSet` / `AsDictionary` (return the source as-is when it already matches the requested type, otherwise materialize a copy).
- **Threading** — `KeyedAsyncLock` (per-key async mutual exclusion, optional timeout-returns-null, sharded, non-reentrant); `HeadlessTaskExtensions` (`Forget`, `WithCancellation`, `GetResultOrDefault`, `WithAggregatedExceptions`, `DelayedAsync`); `HeadlessAsyncExExtensions` (timeout/safe waits over Nito.AsyncEx primitives); `Async.RunSync` / `Async.Using`; `InterlockedExtensions.InterlockedRaiseTo` (lock-free raise-only max on a `ref long` — a CAS loop that never lowers the stored value).
- **LINQ** — `PredicateBuilder` for composing EF Core predicates (`True`/`False` seeds, `And`, `Or`, `Not`, `AndNot`, `OrNot`, and `IEnumerable` folds).
- **IO** — `ReadOnlySequence<byte>.ToStream()`, `NonSeekableStream`, `ActionableStream` (deterministic one-shot dispose action), `ReadSlice` (length-bounded reads), `GetAllText`/`GetAllBytes`(`Async`), `WriteText(Async)`, `CalculateMd5Async`; `FileNames` (untrusted-name sanitization + trusted save names); `FileHelper` (retrying read/write, traversal-safe batch save); `DirectoryHelper` (platform-aware path comparison, sub-directory checks).
- **Constants** — `RegexPatterns` (compiled, ReDoS-hardened: email, URL, IP, slug, national ID, …), `ContentTypes`, `HttpHeaderNames`, `JwtClaimTypes`, `UserClaimTypes`, `LanguageCodes`, `AuthenticationConstants`, `EnvironmentNames` / `EnvironmentVariables`, `HeadlessDiagnostics` (named `ActivitySource` / `Meter` factories), `StorageIdentifier` (per-provider SQL identifier rules), `TimezoneConstants`.
- **Reflection** — `ReflectionHelper` (attribute lookup, cached open-generic subclass checks), `HeadlessTypeExtensions` (friendly names, nullable/enum/`Task<T>` unwrapping, base-class enumeration), `TypeHelper`, `AssemblyInformation` (entry-assembly metadata + commit number).
- **Validation helpers** (boolean checks) — `EgyptianNationalIdValidator` (`IsValid` / `TryParse` → birth date + governorate), `MobilePhoneNumberValidator`, `GeoCoordinateValidator`, `EmailValidator` (HTML5 living standard), `EnumNameValidator` (`IsDefinedName` / cached `GetNames` → string-is-a-defined-enum-member). For FluentValidation rules, use `Headless.FluentValidation`.
- **ID generation & MIME** — `IGuidGenerator` + `SequentialGuidGenerator` (`Version7` / `SqlServer` strategies); `IMimeTypeProvider` / `MimeTypeProvider`.
- **Time** — `FixedTimezoneTimeProvider`, `ChangeableTimezoneTimeProvider`, and date/time/number/string extension methods on BCL types.
- **Claims, HTTP, text, misc.** — `HeadlessClaimsPrincipalExtensions` (claim read/write), `BasicAuthenticationValue`, `HeadlessHttpStatusCodeExtensions`, `LookupNormalizer`, `IgnoreCaseStringComparer`, `DebounceExtensions`, `IpAddressHelper`, `XmlHelper` (XXE-safe XML well-formedness checks — `IsValidXml` / `IsValidXmlAsync` — plus encode/decode), `DisposableFactory`, `OsHelper`.

## Design Notes

- **`Headless.Primitives` and `Headless.Urls` are separate packages.** The value objects, result pattern, paging models, and domain primitives live in `Headless.Primitives`; the URL builder lives in `Headless.Urls`. `Headless.Extensions` references both, so every type they contain stays available to `Headless.Extensions` consumers transitively — the value-object and result notes below apply either way. Consumers who need only the value model or the URL builder can depend on the smaller package directly. See the `Headless.Primitives` and `Headless.Urls` package READMEs.
- **`Money` scaling is scalar-only.** `operator *` and `operator /` take a `decimal` factor and round the result to 2 decimal places with `MidpointRounding.ToEven`; there is no `Money × Money` multiply. `operator +` / `operator -` require matching currency codes and throw otherwise. Comparison operators exist against both `Money` and `decimal`, and `Money` is a total order (mixed-code lists sort by code then amount).
- **`Money` ≠ `MoneyAmount`.** `MoneyAmount` is a source-generated `IPrimitive<decimal>` with no currency code; `GetRounded()` rounds to 2 dp using banker's rounding. Use `Money` when an amount must travel with its code.
- **`Range<T>` uses side-specific infinity semantics for `null` bounds.** A `null` `From` is unbounded below; a `null` `To` is unbounded above. Range-to-range containment, overlap, and `RemoveConflictRangeParts` compare lower and upper bounds separately, so subtracting `[m, p]` from `[null, z]` can return both `[null, predecessor(m)]` and `[successor(p), z]`. The value-level containment overloads still test a single point, so do not use `null` as a concrete point value.
- **Result/error types are value-equal and free of equality-corrupting caches.** `ResultError` is an abstract `record`; its `Code` and `Metadata` are computed on each read rather than stored in a `field`-backed cache, because a lazily-assigned `field` participates in the compiler-generated record `Equals`/`GetHashCode` and silently breaks structural equality. A `default` `ApiResult<T>` / `Result<…>` is an uninitialized failure: reading `.Value` or `.Error` throws — branch on `IsSuccess` first.
- **`KeyedAsyncLock` timeout returns `null`, is non-reentrant, and is sharded.** The timeout overload returns `null` instead of throwing when the wait elapses, so callers can degrade. Per-key semaphores are sharded into 8–64 stripes and reference-counted. Re-entering the same key without releasing deadlocks. The timeout overload takes a `TimeProvider` for deterministic tests.
- **`ParallelForEachAsync` is unordered; `ForEachAsync` is ordered.** `ParallelForEachAsync` delegates to `Parallel.ForEachAsync` (no order guarantee) and defaults to `Environment.ProcessorCount` parallelism (`-1` = unlimited). `ForEachAsync` runs sequentially, preserves order, and offers index and per-item `CancellationToken` overloads.
- **`RegexPatterns` are ReDoS-hardened.** Every pattern is source-generated with `RegexOptions.ExplicitCapture` and a 100 ms `MatchTimeout`, so adversarial input fails fast with `RegexMatchTimeoutException`. `EmailValidator` uses the HTML5 living-standard pattern (permits dot-less domains) with an opt-in `requireDotInDomainName`.
- **`PhoneNumber` canonicalizes to digits and lazy-caches formats.** The national number is stored digits-only, so `"555-1234"` equals `"5551234"`; computed format representations are cached on first use.
- **String comparisons are explicit about culture.** Claim *type* matching is `OrdinalIgnoreCase` while role *values* collect into an ordinal `ImmutableHashSet<string>`; `ExtraProperties` keys use `StringComparer.Ordinal`; `DirectoryHelper` path comparison is case-insensitive on Windows/macOS and case-sensitive on Linux. ID/claim parsing uses `CultureInfo.InvariantCulture`.
- **Reflection assembly loading is trusted-input only.** `AssemblyHelper.LoadAssemblies(...)` loads every matching `.dll`/`.exe` into the default load context, and `InvokeAllStaticMethods(...)` executes public static methods by name. Use these APIs only for application-owned plugin folders or assemblies that have already passed your trust checks; never point them at user-writable upload/cache/temp directories.
- **IO avoids async-over-sync and guards paths.** `GetAllBytesAsync` / `ReadAllBytesAsync` route through framework `File.*` async APIs and pre-size buffers when the length is known; `FileHelper` retries transient `IOException`s three times with exponential backoff and rejects rooted/traversal paths before writing. `ActionableStream` fires its dispose action exactly once across `Dispose` / `Close` / `DisposeAsync`.
- **`TimeUnit` parsing is case-sensitive on `m`.** Suffixes are `nanos`, `micros`, `ms`, `s`, `m` (minutes), `h`, `d`; `m` is case-sensitive to avoid minute/month ambiguity, and overflow surfaces as `false` (`TryParse`) or an exception (`Parse`), never a silent wrap.
- **`EgyptianNationalIdValidator` decodes the century digit and validates a real date.** The leading digit maps `2 → 1900s`, `3 → 2000s` (any other value fails), and the extracted year/month/day are validated through `DateOnly` so impossible dates are rejected.
- **`XmlHelper` parses with an XXE-hardened reader.** `IsValidXml` / `IsValidXmlAsync` (both a `string` and a `Stream` overload) validate well-formedness through a shared `XmlReaderSettings` with `DtdProcessing.Ignore` and `XmlResolver = null`, so inline DTDs are skipped and entity-expansion (billion-laughs) and external-entity (XXE) payloads are never processed. Malformed input returns `false` rather than throwing, so the check is safe on untrusted input.

## Installation

```bash
dotnet add package Headless.Extensions
```

## Quick Start

### Result pattern

```csharp
using Headless.Primitives;

public async Task<ApiResult<User>> GetUserAsync(Guid id, CancellationToken ct)
{
    var user = await _repo.FindAsync(id, ct);
    if (user is null)
        return ApiResult<User>.NotFound(entity: "User", key: id.ToString());

    return user; // implicit conversion from T to ApiResult<T>
}

// Consume without unwrapping.
var message = result.Match(
    success: u => $"Hello {u.Name}",
    failure: error => error.Message
);
```

### Money and MoneyAmount

```csharp
using Headless.Primitives;

var price = new Money(100m, "USD");
var withTax = price * 1.15m;          // scalar scaling -> 115.00 USD (banker's rounding)
var total = price + new Money(20m, "USD"); // same-code addition; throws on code mismatch

var amount = new MoneyAmount(9.875m).GetRounded(); // 9.88 (MidpointRounding.ToEven)
```

### Bounded vs. ordered iteration

```csharp
// Unordered, bounded concurrency (default: Environment.ProcessorCount).
await users.ParallelForEachAsync(degreeOfParallelism: 5, action: async u => await ProcessAsync(u));

// Ordered, sequential, with index + cancellation.
await users.ForEachAsync(async (u, index, token) => await ProcessAsync(u, index, token), ct);
```

### Keyed async locking (stampede protection)

```csharp
using Headless.Threading;

var keyedLock = new KeyedAsyncLock();

// Unbounded: never returns null.
using (await keyedLock.LockAsync(key, ct))
{
    // critical section scoped to `key`
}

// Bounded: returns null on timeout — degrade instead of blocking forever.
using var releaser = await keyedLock.LockAsync(key, TimeSpan.FromSeconds(2), TimeProvider.System, ct);
if (releaser is null)
{
    // timed out — skip work, serve stale, or return a miss
}
```

### EF Core predicate composition

```csharp
using Headless.Linq;

var filter = PredicateBuilder.True<Product>()
    .And(p => p.Price > 0)
    .And(p => p.IsActive);

var products = await dbContext.Products.Where(filter).ToListAsync(ct);
```

## Configuration

None. This package has no options and no DI registration; reference its types and call its extension methods directly.

## Dependencies

- `Headless.Checks`
- `Headless.Primitives` (re-exported)
- `Headless.Urls` (re-exported)
- `Headless.Generator.Primitives` (source generator; analyzer-only)
- `Headless.Generator.Primitives.Abstractions`
- `CommunityToolkit.HighPerformance`
- `Humanizer.Core`
- `libphonenumber-csharp`
- `Microsoft.Bcl.TimeProvider`
- `MimeTypes` (build-time content; backs `MimeTypeProvider`)
- `morelinq`
- `Nito.AsyncEx`
- `Nito.Disposables`
- `Polly.Core`
- `System.Reactive`
- `TimeZoneConverter`

## Side Effects

None. No DI registrations, hosted services, or process-level effects — it is a pure utility library.
