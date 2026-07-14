---
domain: Extensions
packages: Extensions, Primitives, Urls
---

# Extensions

## Table of Contents

- [Quick Orientation](#quick-orientation)
- [Agent Instructions](#agent-instructions)
- [Core Concepts](#core-concepts)
    - [Result pattern vs. exceptions](#result-pattern-vs-exceptions)
    - [Domain primitives and value objects](#domain-primitives-and-value-objects)
    - [GUID ordering strategy](#guid-ordering-strategy)
    - [Deterministic time and timezones](#deterministic-time-and-timezones)
- [Headless.Extensions](#headlessextensions)
    - [Problem Solved](#problem-solved)
    - [Key Features](#key-features)
    - [Design Notes](#design-notes)
    - [Installation](#installation)
    - [Quick Start](#quick-start)
    - [Configuration](#configuration)
    - [Dependencies](#dependencies)
    - [Side Effects](#side-effects)
- [Headless.Primitives](#headlessprimitives)
    - [Problem Solved](#problem-solved-1)
    - [Key Features](#key-features-1)
    - [Design Notes](#design-notes-1)
    - [Installation](#installation-1)
    - [Quick Start](#quick-start-1)
    - [Configuration](#configuration-1)
    - [Dependencies](#dependencies-1)
    - [Side Effects](#side-effects-1)
- [Headless.Urls](#headlessurls)
    - [Problem Solved](#problem-solved-2)
    - [Key Features](#key-features-2)
    - [Design Notes](#design-notes-2)
    - [Installation](#installation-2)
    - [Quick Start](#quick-start-2)
    - [Configuration](#configuration-2)
    - [Dependencies](#dependencies-2)
    - [Side Effects](#side-effects-2)

> Foundational extension methods, domain primitives, value objects, the result pattern, and collection/IO/threading/reflection helpers shared across every Headless package.

## Quick Orientation

`Headless.Extensions` is the framework's base library — almost every other `Headless.*` package depends on it. It has no DI registration and no configuration; you reference types and call extension methods directly. The surface is organized by namespace, each solving one category of repetitive utility code. The `Headless.Primitives` (value objects, result pattern, paging) and `Headless.Urls` (URL builder) namespaces ship as **separate packages** that `Headless.Extensions` references — so every type below remains available to `Headless.Extensions` consumers transitively, but a consumer who only needs the value model or URL builder can depend on the smaller package directly. Both are documented as their own sections below.

- **`Headless.Primitives`** (separate package) — the result pattern (`ApiResult`, `ApiResult<T>`, `Result<TValue, TError>`, `Result<TError>`, `ResultError` hierarchy, `ErrorDescriptor`), source-generated domain primitives (`UserId`, `AccountId`, `MoneyAmount`, `Month`, `PhoneNumber`), validated value objects (`Money`, `GeoCoordinate`, `FullGeoCoordinate`, `Range<T>`, `PreferredLocale`, `TimeUnit`), pagination (`IndexPage<T>`, `ContinuationPage<T>`), `ExtraProperties`, `AsyncEvent<T>`, `NameValue<T>`, `OrderBy`, `File`/`Image`, `PageMetadata`, `TenantInformation`.
- **`Headless.Urls`** (separate package) — a fluent, mutable `Url` builder/parser plus `QueryParamCollection`, derived from Flurl (MIT).
- **`Headless.Collections`** — `ParallelForEachAsync` / `ForEachAsync`, `DetectChanges` (added/removed/updated classification), `EquatableArray<T>`, `ComparerFactory`, `TypeList`.
- **`Headless.Linq`** — `PredicateBuilder` for composing EF Core `Expression<Func<T,bool>>` filters (`And`, `Or`, `Not`, `True`/`False`).
- **`Headless.Threading`** — `KeyedAsyncLock` (per-key async mutual exclusion), `TaskExtensions` (`Forget`, `WithCancellation`, `GetResultOrDefault`), `AsyncExExtensions`, `Async` (`RunSync`, deterministic `Using`), `InterlockedExtensions` (`InterlockedRaiseTo` — lock-free raise-only max on a `ref long`).
- **`Headless.IO`** — stream helpers (`ReadOnlySequenceStream` via `ToStream()`, `NonSeekableStream`, `ActionableStream`, `ReadSlice`, `GetAllText`/`GetAllBytes`, `CalculateMd5Async`), `FileNames` (untrusted-name sanitization), `FileHelper` (resilient read/write), `DirectoryHelper`.
- **`Headless.Constants`** — `RegexPatterns`, `ContentTypes`, `HttpHeaderNames`, `JwtClaimTypes`, `UserClaimTypes`, `LanguageCodes`, `AuthenticationConstants`, `EnvironmentNames`/`EnvironmentVariables`, `HeadlessDiagnostics`, `StorageIdentifier`, `TimezoneConstants`.
- **`Headless.Reflection`** — `ReflectionHelper` (attribute lookup, generic subclass checks), `TypeExtensions` (friendly names, nullable/Task unwrapping), `TypeHelper`, `AssemblyInformation`.
- **`Headless.Validators`** — `EgyptianNationalIdValidator`, `MobilePhoneNumberValidator`, `GeoCoordinateValidator`, `EmailValidator`, `EnumNameValidator` (boolean checks, not FluentValidation rules — see [utilities.md](utilities.md) for `Headless.FluentValidation`).
- **`Headless.Abstractions`** — `IGuidGenerator`, `SequentialGuidType`, `SequentialGuidGenerator`, `IMimeTypeProvider`/`MimeTypeProvider`.
- **Smaller namespaces** — claims (`ClaimsPrincipalExtensions`), `Headless.Http` (`BasicAuthenticationValue`, `HttpStatusCodeExtensions`), `Headless.Text` (`LookupNormalizer`, `IgnoreCaseStringComparer`, `FormattedStringValueExtractor`), `Headless.Network` (`IpAddressHelper`), `Headless.Xml` (`XmlHelper`), `Headless.UI` (`DebounceExtensions`), `Headless.Exceptions` (`EntityNotFoundException`, `ConflictException`, `UnauthorizedException`), plus extension methods on BCL types in the `System.*` namespaces (`StringExtensions`, `DateTimeExtensions`, `NumberExtensions`, `EnumExtensions`, `EnumerableExtensions`, …).

For strongly-typed primitives **you define yourself**, use the source generator in `Headless.Generator.Primitives` (see [utilities.md](utilities.md)); the primitives listed above are the framework's built-in ones generated by it.

## Agent Instructions

- Return `ApiResult<T>` / `ApiResult` from service methods for **expected** failures (not found, conflict, validation) instead of throwing. Use `Result<TValue, TError>` when you need a custom error type. Reserve exceptions for programmer errors and truly exceptional conditions.
- Do **not** read `.Value` or `.Error` on a `default` `ApiResult<T>` / `Result<…>` — a default (uninitialized) instance is a failure with no error and throws on access. Always branch on `IsSuccess` / `TryGetValue` first.
- Do **not** lazy-cache a computed property via the `field` keyword on any `record` in this package's result/error types (or your own). `ResultError.Code` / `Metadata` are deliberately computed on each read so structural record equality stays correct — a `field`-backed cache silently corrupts `Equals`/`GetHashCode`.
- `Money` arithmetic is **scalar-only** for scaling: `*` and `/` take a `decimal` (e.g. `price * 1.15m`), not another `Money`. `+` and `-` take a `Money` and throw if the currency codes differ. There is no `Money × Money` multiply.
- `MoneyAmount` is a plain `decimal` primitive with **no currency code**. Use `GetRounded()` for 2-decimal banker's rounding (`MidpointRounding.ToEven`). Use `Money` when you need an amount paired with a currency code.
- `Range<T>` treats `null` bounds as infinities: `From == null` means unbounded below, and `To == null` means unbounded above. Range-to-range containment, overlap, and `RemoveConflictRangeParts` follow those side-specific semantics; do not use `null` as a concrete point value.
- `KeyedAsyncLock.LockAsync(key, timeout, timeProvider, ct)` returns `null` on timeout (it does **not** throw) — check the releaser for null and degrade. The unbounded `LockAsync(key, ct)` overload never returns null. The lock is **non-reentrant**: re-acquiring the same key on the same flow without releasing deadlocks.
- `ParallelForEachAsync` does **not** preserve order and defaults to `Environment.ProcessorCount` degree of parallelism (pass `-1` for unlimited). Use `ForEachAsync` when you need ordered, sequential execution with index/cancellation support.
- Regex access via `RegexPatterns.*` uses a 100 ms match timeout plus `ExplicitCapture` (ReDoS hardening). Matching untrusted input can throw `RegexMatchTimeoutException` — handle it on hostile inputs.
- Register GUID generation with `AddHeadlessGuidGenerator()` from `Headless.Core` (not this package). Resolve `IGuidGenerator` by `SequentialGuidType.Version7` for byte-ordered stores (the default) and only by `SequentialGuidType.SqlServer` for SQL Server clustered primary keys — `Version7` fragments SQL Server indexes.
- Use `EgyptianNationalIdValidator.IsValid(...)` / `TryParse(...)` and the other `Headless.Validators.*` helpers for boolean checks. For FluentValidation **rules** (`InternationalPhoneNumber()`, `EgyptianNationalId()`, …) use `Headless.FluentValidation` instead (see [utilities.md](utilities.md)).
- Use `ClaimsPrincipalExtensions` (`GetUserId()`, `GetRoles()`, `GetTenantId()`, `AddOrReplace(...)`) for claim reads/writes. `GetRoles()` returns an empty `ImmutableHashSet<string>` (never null) and role values use an ordinal set; role *type* matching is `OrdinalIgnoreCase`.
- Use the `Headless.IO` stream helpers (`stream.GetAllBytesAsync()`, `stream.GetAllText()`) rather than hand-rolling buffers — they avoid async-over-sync, pre-size buffers when the length is known, and retry transient `IOException`s.
- Use `AssemblyHelper.LoadAssemblies(...)` / `InvokeAllStaticMethods(...)` only with trusted, application-owned plugin folders or assemblies that have already passed your trust checks. They load assemblies into the default load context and can execute public static methods by name; never point them at user-writable upload/cache/temp directories.
- `TimeUnit.Parse("5m")` is **case-sensitive** on the `m` suffix (`m` = minutes); this avoids month/minute ambiguity. Invalid magnitudes surface as `false` from `TryParse` / an exception from `Parse`, never a silent wrap.
- `action.Debounce(interval, timeProvider?, onError?)` (`Headless.UI`) coalesces rapid calls into a single trailing-edge run with the **latest** arguments. The wrapped action runs on a thread-pool timer callback, so an exception it throws is routed to the optional `onError` handler — or **swallowed** when none is supplied — and never propagates (an unhandled throw on that callback would crash the process). A generation guard skips superseded schedules, so a call landing as a prior timer fires does not double-run with stale arguments.

## Core Concepts

`Headless.Extensions` is mostly mechanical helpers, but four ideas shape how the rest of the framework uses it: how failures are modeled, how domain values are typed, how GUIDs are ordered for databases, and how time is made testable.

### Result pattern vs. exceptions

The package models **expected** outcomes as values, not exceptions. `ApiResult` / `ApiResult<T>` carry either a value or a `ResultError`; `Result<TValue, TError>` / `Result<TError>` are the same idea with a caller-chosen error type. `ResultError` is an abstract `record` with a small closed hierarchy — `NotFoundError`, `UnauthorizedError`, `ForbiddenError`, `ConflictError`, `ValidationError`, `AggregateError` — each exposing a machine-readable `Code` and a human `Message`. Factory shortcuts (`ApiResult<T>.NotFound(entity, key)`, `.Conflict(code, message)`, `.ValidationFailed(...)`, `.Forbidden(reason)`, `.Unauthorized()`) keep call sites terse, and `Match` / `Map` / `Bind` (plus their `…Async` variants) let you compose without unwrapping. Throw only for programmer errors; return a result for anything a caller is expected to handle. The `Headless.Exceptions` types (`EntityNotFoundException`, `ConflictException`) exist for the exception-based path when a layer genuinely needs to throw.

### Domain primitives and value objects

Two distinct mechanisms produce strongly-typed values. **Source-generated primitives** (`UserId`, `AccountId`, `MoneyAmount`, `Month`, `PhoneNumber`) implement `IPrimitive<T>` and are emitted by `Headless.Generator.Primitives` with equality, JSON, and TypeConverter support — they wrap a single underlying value and validate it on creation. **Hand-written value objects** (`Money`, `GeoCoordinate`, `FullGeoCoordinate`, `Range<T>`, `PreferredLocale`) validate their inputs in the constructor/init and throw on invalid data, so an instance that exists is always valid (e.g. a `GeoCoordinate` can never hold a latitude outside `[-90, 90]`). Both give you value equality. The practical consequence: prefer these over raw `Guid`/`decimal`/`(double, double)` in domain signatures so invalid states are unrepresentable.

### GUID ordering strategy

Random GUIDs (`Guid.NewGuid()`) fragment clustered database indexes. `IGuidGenerator` abstracts the ordering choice via `SequentialGuidType`: `Version7` produces RFC 9562 UUIDv7 values whose timestamp sorts correctly for **byte-ordered** stores (PostgreSQL, most key-value stores) and is the framework default; `SqlServer` produces EF Core "comb" GUIDs whose bytes are arranged for SQL Server's `uniqueidentifier` sort order. Picking the wrong one is a real performance footgun — `Version7` fragments SQL Server clustered keys because SQL Server sorts the timestamp bytes last. Registration lives in `Headless.Core` (`AddHeadlessGuidGenerator()`, see [core.md](core.md)); persisted backends should resolve the keyed strategy that matches their storage rather than the unkeyed default.

### Deterministic time and timezones

The package builds on `TimeProvider` so time is injectable and testable. `FixedTimezoneTimeProvider` pins a `TimeZoneInfo` for deterministic local-time conversions; `ChangeableTimezoneTimeProvider` allows a scoped `ChangeTimeZone(...)` that restores on dispose (single-writer; nested changes restore out of order). `TimezoneConstants` resolves common IANA zones cross-platform via `TimeZoneConverter`. Combined with the date/time extension methods (`DateTimeExtensions`, `DateTimeOffsetExtensions`, `TimeSpanExtensions`), this lets domain code avoid `DateTime.UtcNow` — which the Headless SDK bans at compile time (`RS0030`) — and stay unit-testable. `DateTimeExtensions.NormalizeToUtc()` is the safe way to coerce a `DateTime` of untrusted `Kind` (external SDKs often return `Unspecified`) to UTC before it becomes a `DateTimeOffset`. For the `ICurrentTimeZone` abstraction, see [core.md](core.md); for which clock owns which decision, see [temporal-authority-standard](../solutions/design-patterns/temporal-authority-standard.md).

---

## Headless.Extensions

The framework's base utility library: extension methods, domain primitives, value objects, the result pattern, and common helpers referenced by nearly every other `Headless.*` package.

### Problem Solved

Eliminates repetitive utility code — result/error modeling, strongly-typed domain values, parallel and keyed-async helpers, stream and file IO, reflection, validation, and a large catalog of constants — by providing one battle-tested, dependency-light base library so each downstream package does not re-implement the same primitives.

### Key Features

- **Result pattern** — `ApiResult` / `ApiResult<T>` with built-in error factories (`NotFound`, `Conflict`, `ValidationFailed`, `Forbidden`, `Unauthorized`); `Result<TValue, TError>` / `Result<TError>` for custom error types; the `ResultError` record hierarchy (`NotFoundError`, `UnauthorizedError`, `ForbiddenError`, `ConflictError`, `ValidationError`, `AggregateError`); `ErrorDescriptor` for structured, severity-tagged API errors; `Match`/`Map`/`Bind` combinators with `…Async` overloads.
- **Domain primitives** (source-generated, with JSON + TypeConverter support) — `UserId`, `AccountId`, `MoneyAmount` (decimal amount, banker's rounding), `Month` (1–12), `PhoneNumber` (libphonenumber-backed, digits-only canonicalization).
- **Value objects** (validated on construction) — `Money` (amount + currency code, scalar `*`/`/`, same-code `+`/`-`, total ordering), `GeoCoordinate` / `FullGeoCoordinate` (range-checked lat/long, Haversine distance), `Range<T>` (inclusive/exclusive containment, overlap), `PreferredLocale`, `TimeUnit` (duration-string parsing), `NameValue<T>`, `OrderBy`, `ExtraProperties` (ordinal-keyed property bag), `TenantInformation`, `PageMetadata`, `File` / `Image`.
- **Pagination** — `IndexPageRequest` / `IndexPage<T>` (offset) and `ContinuationPageRequest` / `ContinuationPage<T>` (cursor), each with `Select` / `Where` projection.
- **Collections** — `ParallelForEachAsync` (bounded concurrency) and `ForEachAsync` (ordered sequential, with index/cancellation overloads); `DetectChanges` (added/removed/updated/unchanged classification by key); `EquatableArray<T>` (value-equality array wrapper); `ComparerFactory`; `TypeList` / `ITypeList`; `EnumerableExtensions` materialization helpers — `AsList` / `AsArray` / `AsICollection` / `AsIList` (→ `IList<T>`) / `AsIReadOnlyCollection` / `AsIReadOnlyList` (→ `IReadOnlyList<T>`) / `AsISet` / `AsHashSet` / `AsDictionary` (return the source as-is when it already matches the requested type, otherwise materialize a copy).
- **Threading** — `KeyedAsyncLock` (per-key async mutual exclusion, optional timeout-returns-null, sharded, non-reentrant); `TaskExtensions` (`Forget`, `WithCancellation`, `GetResultOrDefault`, `WithAggregatedExceptions`, `DelayedAsync`); `AsyncExExtensions` (timeout/safe waits over Nito.AsyncEx primitives); `Async.RunSync` / `Async.Using`; `InterlockedExtensions.InterlockedRaiseTo` (lock-free raise-only max on a `ref long`).
- **LINQ** — `PredicateBuilder` for composing EF Core predicates (`True`/`False` seeds, `And`, `Or`, `Not`, `AndNot`, `OrNot`, and `IEnumerable` folds).
- **IO** — `ReadOnlySequence<byte>.ToStream()`, `NonSeekableStream`, `ActionableStream` (deterministic one-shot dispose action), `ReadSlice` (length-bounded reads), `GetAllText`/`GetAllBytes`(`Async`), `WriteText(Async)`, `CalculateMd5Async`; `FileNames` (untrusted-name sanitization + trusted save names); `FileHelper` (retrying read/write, traversal-safe batch save); `DirectoryHelper` (platform-aware path comparison, sub-directory checks).
- **Constants** — `RegexPatterns` (compiled, ReDoS-hardened: email, URL, IP, slug, national ID, …), `ContentTypes`, `HttpHeaderNames`, `JwtClaimTypes`, `UserClaimTypes`, `LanguageCodes`, `AuthenticationConstants`, `EnvironmentNames` / `EnvironmentVariables`, `HeadlessDiagnostics` (named `ActivitySource` / `Meter` factories), `StorageIdentifier` (per-provider SQL identifier rules), `TimezoneConstants`.
- **Reflection** — `ReflectionHelper` (attribute lookup, cached open-generic subclass checks), `TypeExtensions` (friendly names, nullable/enum/`Task<T>` unwrapping, base-class enumeration), `TypeHelper`, `AssemblyInformation` (entry-assembly metadata + commit number).
- **Validation helpers** (boolean checks) — `EgyptianNationalIdValidator` (`IsValid` / `TryParse` → birth date + governorate), `MobilePhoneNumberValidator`, `GeoCoordinateValidator`, `EmailValidator` (HTML5 living standard), `EnumNameValidator` (`IsDefinedName` / cached `GetNames` → string is a defined enum member).
- **ID generation & MIME** — `IGuidGenerator` + `SequentialGuidGenerator` (`Version7` / `SqlServer` strategies); `IMimeTypeProvider` / `MimeTypeProvider`.
- **Time** — `FixedTimezoneTimeProvider`, `ChangeableTimezoneTimeProvider`, and date/time/number/string extension methods on BCL types.
- **Claims, HTTP, text, misc.** — `ClaimsPrincipalExtensions` (claim read/write), `BasicAuthenticationValue`, `HttpStatusCodeExtensions`, `LookupNormalizer`, `IgnoreCaseStringComparer`, `DebounceExtensions`, `IpAddressHelper`, `XmlHelper` (XXE-safe XML well-formedness checks — `IsValidXml` / `IsValidXmlAsync` — plus encode/decode), `DisposableFactory`, `OsHelper`.

### Design Notes

- **`Money` scaling is scalar-only.** `operator *` and `operator /` take a `decimal` factor and round the result to 2 decimal places with `MidpointRounding.ToEven`; there is no `Money × Money` multiply. `operator +` / `operator -` require matching currency codes and throw otherwise. Comparison operators exist against both `Money` and `decimal`, and `Money` is a total order (mixed-code lists sort by code then amount). This shape prevents the nonsensical "money squared" result and keeps rounding centralized.
- **`Money` ≠ `MoneyAmount`.** `MoneyAmount` is a source-generated `IPrimitive<decimal>` with no currency code; `GetRounded()` rounds to 2 dp using banker's rounding. Use `Money` when an amount must travel with its code. Do not treat `MoneyAmount` as currency-aware.
- **`Range<T>` uses side-specific infinity semantics for `null` bounds.** A `null` `From` is unbounded below; a `null` `To` is unbounded above. Range-to-range containment, overlap, and `RemoveConflictRangeParts` compare lower and upper bounds separately, so subtracting `[m, p]` from `[null, z]` can return both `[null, predecessor(m)]` and `[successor(p), z]`. The value-level containment overloads still test a single point, so do not use `null` as a concrete point value.
- **Result/error types are value-equal and free of equality-corrupting caches.** `ResultError` is an abstract `record`; its `Code` and `Metadata` are computed on each read rather than stored in a `field`-backed cache, because a lazily-assigned `field` participates in the compiler-generated record `Equals`/`GetHashCode` and silently breaks structural equality. A `default` `ApiResult<T>` / `Result<…>` is an uninitialized failure: reading `.Value` or `.Error` throws — branch on `IsSuccess` first.
- **`KeyedAsyncLock` timeout returns `null`, is non-reentrant, and is sharded.** The timeout overload returns `null` instead of throwing when the wait elapses, so callers can degrade (skip work / serve stale). The internal semaphore dictionary is sharded into 8–64 stripes (~`ProcessorCount`) to cut contention, and per-key semaphores are reference-counted and removed at zero. Re-entering the same key without releasing deadlocks. The timeout overload takes a `TimeProvider`, so tests can drive it with `FakeTimeProvider`.
- **`ParallelForEachAsync` is unordered; `ForEachAsync` is ordered.** `ParallelForEachAsync` delegates to `Parallel.ForEachAsync` (no result order guarantee) and defaults to `Environment.ProcessorCount` parallelism (`-1` = unlimited). `ForEachAsync` runs sequentially, preserves order, checks cancellation before each element, and offers index and per-item `CancellationToken` overloads. Pick deliberately.
- **`RegexPatterns` are ReDoS-hardened.** Every pattern is source-generated with `RegexOptions.ExplicitCapture` and a 100 ms `MatchTimeout`, so adversarial input fails fast with `RegexMatchTimeoutException` instead of hanging. `EmailValidator` uses the HTML5 living-standard email pattern (a willful RFC 5321/5322 deviation that permits dot-less domains) with an opt-in `requireDotInDomainName`.
- **`PhoneNumber` canonicalizes to digits and lazy-caches formats.** The national number is stored digits-only, so `"555-1234"` equals `"5551234"`; computed representations (national/international/normalized format) are cached on first use. Construction validates a positive country code.
- **String comparisons are explicit about culture.** Claim *type* matching is `OrdinalIgnoreCase` while role *values* collect into an ordinal `ImmutableHashSet<string>`; `ExtraProperties` keys use `StringComparer.Ordinal`; identifier/path comparisons in `DirectoryHelper` are case-insensitive on Windows/macOS and case-sensitive on Linux. ID/claim parsing uses `CultureInfo.InvariantCulture`.
- **Reflection assembly loading is trusted-input only.** `AssemblyHelper.LoadAssemblies(...)` loads every matching `.dll`/`.exe` into the default load context, and `InvokeAllStaticMethods(...)` executes public static methods by name. Use these APIs only for application-owned plugin folders or assemblies that have already passed your trust checks; never point them at user-writable upload/cache/temp directories.
- **IO avoids async-over-sync and guards paths.** `GetAllBytesAsync` / `ReadAllBytesAsync` route through framework `File.*` async APIs and pre-size buffers when the stream length is known; `FileHelper` retries transient `IOException`s three times with exponential backoff (Polly) and rejects rooted paths and traversal sequences before writing; batch save bounds concurrency to `ProcessorCount`. `ActionableStream` fires its dispose action exactly once across `Dispose` / `Close` / `DisposeAsync` and still disposes the inner stream if the action throws.
- **`TimeUnit` parsing is case-sensitive on `m`.** Suffixes are `nanos`, `micros`, `ms`, `s`, `m` (minutes), `h`, `d`; `m` is case-sensitive to avoid minute/month ambiguity, parsing trims the input span rather than allocating a lowercased copy, and overflow surfaces as `false` (`TryParse`) or an exception (`Parse`), never a silent wrap.
- **`EgyptianNationalIdValidator` decodes the century digit and validates a real date.** The leading digit maps `2 → 1900s`, `3 → 2000s` (any other value fails), and the extracted year/month/day are validated through `DateOnly` so impossible dates (e.g. 30 February) are rejected; the governorate code maps through an ordinal `FrozenDictionary`.
- **`XmlHelper` parses with an XXE-hardened reader.** `IsValidXml` / `IsValidXmlAsync` (both a `string` and a `Stream` overload) validate well-formedness through a shared `XmlReaderSettings` with `DtdProcessing.Ignore` and `XmlResolver = null`, so inline DTDs are skipped and entity-expansion (billion-laughs) and external-entity (XXE) payloads are never processed. Malformed input returns `false` rather than throwing, so the check is safe on untrusted input.

### Installation

```bash
dotnet add package Headless.Extensions
```

### Quick Start

#### Result pattern

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

#### Money and MoneyAmount

```csharp
using Headless.Primitives;

var price = new Money(100m, "USD");
var withTax = price * 1.15m;          // scalar scaling -> 115.00 USD (banker's rounding)
var total = price + new Money(20m, "USD"); // same-code addition; throws on code mismatch

var amount = new MoneyAmount(9.875m).GetRounded(); // 9.88 (MidpointRounding.ToEven)
```

#### Bounded vs. ordered iteration

```csharp
// Unordered, bounded concurrency (default: Environment.ProcessorCount).
await users.ParallelForEachAsync(degreeOfParallelism: 5, action: async u => await ProcessAsync(u));

// Ordered, sequential, with index + cancellation.
await users.ForEachAsync(async (u, index, token) => await ProcessAsync(u, index, token), ct);
```

#### Keyed async locking (stampede protection)

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

#### EF Core predicate composition

```csharp
using Headless.Linq;

var filter = PredicateBuilder.True<Product>()
    .And(p => p.Price > 0)
    .And(p => p.IsActive);

var products = await dbContext.Products.Where(filter).ToListAsync(ct);
```

#### Egyptian national ID

```csharp
using Headless.Validators;

if (EgyptianNationalIdValidator.IsValid("29901011234567")
    && EgyptianNationalIdValidator.TryParse("29901011234567", out var year, out var month, out var day, out var governorate))
{
    var birthDate = new DateOnly(year, month, day);
    // governorate is the Arabic governorate name
}
```

### Configuration

None. This package has no options and no DI registration; reference its types and call its extension methods directly.

### Dependencies

- `Headless.Checks`
- `Headless.Primitives` (re-exported; see its section below)
- `Headless.Urls` (re-exported; see its section below)
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

### Side Effects

None. No DI registrations, hosted services, or process-level effects — it is a pure utility library.

---

## Headless.Primitives

### Problem Solved

Domain code that passes raw `Guid`, `decimal`, or `(double, double)`, or throws-and-catches for expected failures, loses intent and permits invalid states. `Headless.Primitives` supplies the framework's shared value model: a result pattern for expected failures, validated value objects that cannot hold invalid data, and consistent paging and error shapes.

### Key Features

- Result pattern: `ApiResult`, `ApiResult<T>`, `Result<TValue, TError>`, `Result<TError>`, the `ResultError` hierarchy, `ErrorDescriptor`, `ApiResultError`.
- Source-generated domain primitives: `UserId`, `AccountId`, `MoneyAmount`, `Month`, `PhoneNumber` (implement `IPrimitive<T>`).
- Hand-written value objects: `Money`, `GeoCoordinate`, `FullGeoCoordinate`, `Range<T>`, `PreferredLocale`, `TimeUnit`.
- Paging: `IndexPage<T>`, `IndexPageRequest`, `ContinuationPage<T>`, `ContinuationPageRequest`, `PageMetadata`, `OrderBy`, `IHasOrderByRequest` / `IHasMultiOrderByRequest`.
- Misc: `ExtraProperties` / `IHasExtraProperties`, `Locales` / `LocaleAttribute`, `AsyncEvent<T>`, `NameValue` / `NameValue<T>`, `File` / `Image`, `TenantInformation`.

### Design Notes

Split out of `Headless.Extensions` so a consumer can depend on the value model without the full base library; `Headless.Extensions` keeps a `ProjectReference`, so these types stay transitively available. `OrderBy(string Property, bool Ascending = true)` defaults to **ascending** (a bare `new OrderBy("Name")` sorts ascending, matching the near-universal convention). `ExtraProperties` and `Locales` derive from `Dictionary<,>` (the established `IHasExtraProperties` pattern) and are `sealed`.

### Installation

```bash
dotnet add package Headless.Primitives
```

### Quick Start

```csharp
using Headless.Primitives;

public async Task<ApiResult<User>> GetUserAsync(Guid id, CancellationToken ct)
{
    var user = await _repo.FindAsync(id, ct);
    if (user is null)
        return ApiResult<User>.NotFound(entity: "User", key: id.ToString());

    return user; // implicit conversion from T to ApiResult<T>
}

var order = new OrderBy("CreatedAt");                    // ascending by default
var descending = new OrderBy("CreatedAt", Ascending: false);
```

### Configuration

None. Types are constructed directly; no DI registration or options.

### Dependencies

- `Headless.Checks`
- `Headless.Generator.Primitives.Abstractions`
- `Headless.Generator.Primitives` (source generator; analyzer-only)
- `libphonenumber-csharp`

### Side Effects

None. No DI registrations or ambient state.

---

## Headless.Urls

### Problem Solved

Assembling and editing URLs with string concatenation is error-prone — double slashes, missing encoding, duplicated query parameters, fragile parsing. `Headless.Urls` provides a mutable `Url` builder and an ordered, multi-value `QueryParamCollection` that compose and edit path segments, query parameters, and fragments without hand-rolled encoding.

### Key Features

- `Url` mutable builder with `Scheme` / `Host` / `Path` / `Query` / `Fragment` accessors.
- `Url.Parse` / `Url.ParseQueryParams` / `Url.ParsePathSegments` parsing entry points.
- `AppendPathSegment(s)`, `SetQueryParam` / `AppendQueryParam` / `RemoveQueryParam` with `NullValueHandling` control.
- `QueryParamCollection` — ordered, duplicate-preserving query-parameter store.

### Design Notes

Derived from [Flurl](https://github.com/tmenier/Flurl)'s `Flurl.Url` API (MIT); attributed in the package's `THIRD-PARTY-NOTICES.md`. Packaged separately from `Headless.Extensions` so URL-only consumers do not pull the base library. `NullValueHandling` has explicit backing values (`NameOnly = 0`, `Remove = 1`, `Ignore = 2`) that must not be reordered.

### Installation

```bash
dotnet add package Headless.Urls
```

### Quick Start

```csharp
using Headless.Urls;

var url = Url.Parse("https://api.example.com")
    .AppendPathSegment("v1")
    .AppendPathSegments("users", "42")
    .SetQueryParam("include", "profile");

string result = url.ToString();
// https://api.example.com/v1/users/42?include=profile
```

### Configuration

None. `Url` is constructed directly; no DI registration or options.

### Dependencies

- `Headless.Checks`

### Side Effects

None. Pure value/builder types with no DI registration or ambient state.
