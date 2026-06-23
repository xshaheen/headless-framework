# Headless.Extensions

Foundational utility library providing extension methods, primitives, helpers, and common abstractions used throughout the framework.

## Problem Solved

Eliminates repetitive utility code across projects by providing a comprehensive set of battle-tested extensions, helpers, and primitives for common operations (strings, collections, dates, IO, reflection, etc.).

## Key Features

- **Result Pattern**:
  - `ApiResult` / `ApiResult<T>` - Success/failure with built-in error factories (`NotFound`, `Conflict`, `Forbidden`, `Unauthorized`, `ValidationFailed`)
  - `Result<TValue, TError>` / `Result<TError>` - Flexible result types with custom error types
  - `ResultError` hierarchy - `NotFoundError`, `UnauthorizedError`, `ForbiddenError`, `ConflictError`, `ValidationError`, `AggregateError`
  - `ErrorDescriptor` - Standardized error reporting with code, description, severity, and params

- **Primitives** (Source-generated with JSON/TypeConverter support):
  - `UserId` / `AccountId` - Strongly-typed identifiers
  - `Money` - Currency-aware decimal with arithmetic operators
  - `Month` - Month representation
  - `PhoneNumber` - E.164 phone number
  - `Image` / `File` - Media metadata
  - `PageMetadata` - SEO metadata
  - `TenantInformation` - Tenant data

- **Domain Value Objects**: `Currency`, `GeoCoordinate`, `FullGeoCoordinate`, `Range<T>`, `PreferredLocale`, `OrderBy`, `NameValue<T>`, `ExtraProperties`, `Locales`, `TimeUnit`

- **ID Generation**: `IGuidGenerator` (`SequentialGuidGenerator` supporting time-ordered `Version7` and SQL Server comb `SqlServer` strategies)

- **Pagination**: `IndexPageRequest`/`IndexPage<T>` and `ContinuationPageRequest`/`ContinuationPage<T>`

- **Collections**: `ParallelForEachAsync`, `DetectChanges`, `EquatableArray<T>`, `ComparerFactory`, `TypeList`

- **Threading**: `KeyedAsyncLock` - per-key async mutual exclusion (e.g. cache-stampede protection) with an optional `TimeProvider`-driven acquisition timeout that returns `null` instead of blocking when the wait elapses

- **LINQ**: `PredicateBuilder` for composing EF Core expressions (`And`, `Or`, `Not`)

- **Dates & Time**: Fluent date manipulation, `TimeProvider` extensions, timezone conversion, `ChangeableTimezoneTimeProvider`

- **Strings**: Humanize integration, truncation, manipulation helpers

- **Constants**: `RegexPatterns` (email, URL, IP, etc.), `ContentTypes`, `HttpHeaderNames`, `JwtClaimTypes`, `UserClaimTypes`, `LanguageCodes`

- **Reflection**: Fast property accessors, type scanning, IL emit helpers, `AssemblyInformation`

- **HTTP**: `BasicAuthenticationValue`, `HttpStatusCodeExtensions`

- **Exceptions**: `EntityNotFoundException`, `ConflictException`

- **Validation**: `MobilePhoneNumberValidator`, `GeoCoordinateValidator`, `EmailValidator`, `EgyptianNationalIdValidator`

- **Helpers**: `OsHelper`, `DisposableFactory`, `IpAddressHelper`

## Installation

```bash
dotnet add package Headless.Extensions
```

## Quick Start

### Result Pattern

```csharp
public async Task<ApiResult<User>> GetUserAsync(Guid id)
{
    var user = await _repo.GetByIdAsync(id);
    if (user is null)
        return ApiResult<User>.NotFound();

    return ApiResult<User>.Ok(user);
}
```

### Collection Extensions

```csharp
await users.ParallelForEachAsync(async user => await ProcessUserAsync(user), maxDegreeOfParallelism: 5);
```

### Change Detection

```csharp
var (added, removed, existing) = oldItems.DetectChanges(newItems, areSameEntity: (old, @new) => old.Id == @new.Id);
```

### Expression Composition

```csharp
var filter = PredicateBuilder.True<Product>().And(p => p.Price > 0).And(p => p.IsActive);

var products = await dbContext.Products.Where(filter).ToListAsync();
```

### Keyed Async Locking

```csharp
var keyedLock = new KeyedAsyncLock();

// Unbounded: wait until this key's lock is available.
using (await keyedLock.LockAsync(key, cancellationToken))
{
    // critical section scoped to `key`
}

// Bounded: returns null if the lock is not acquired within the timeout. TimeProvider drives the
// wait, so the timeout is deterministic under FakeTimeProvider in tests.
using var releaser = await keyedLock.LockAsync(key, TimeSpan.FromSeconds(2), TimeProvider.System, cancellationToken);
if (releaser is null)
{
    // timed out acquiring the lock — degrade (skip work, serve stale, or return a miss)
}
```

## Configuration

No configuration required.

## Dependencies

- `Headless.Checks`
- `Headless.Generator.Primitives` (source generator)
- `Headless.Generator.Primitives.Abstractions`
- `CommunityToolkit.HighPerformance`
- `Humanizer.Core`
- `libphonenumber-csharp`
- `Microsoft.Bcl.TimeProvider`
- `MimeTypes`
- `morelinq`
- `Nito.AsyncEx`
- `Nito.Disposables`
- `Polly.Core`
- `System.Reactive`
- `TimeZoneConverter`

## Side Effects

None.
