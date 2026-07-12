# Headless.Primitives

Value objects, the result pattern, paging models, and domain primitives.

## Problem Solved

Domain code that passes raw `Guid`, `decimal`, `(double, double)`, or throws-and-catches for expected failures
loses intent and lets invalid states exist. `Headless.Primitives` supplies the framework's shared building
blocks: a result pattern for expected failures, validated value objects that cannot hold invalid data, and
consistent paging and error-descriptor shapes so every package models these the same way.

## Key Features

- **Result pattern**: `ApiResult`, `ApiResult<T>`, `Result<TValue, TError>`, `Result<TError>`, the `ResultError`
  hierarchy, `ErrorDescriptor`, and `ApiResultError` — model expected failure without exceptions.
- **Source-generated domain primitives**: `UserId`, `AccountId`, `MoneyAmount`, `Month`, `PhoneNumber` (implement
  `IPrimitive<T>`, emitted by `Headless.Generator.Primitives` with equality, JSON, and TypeConverter support).
- **Hand-written value objects**: `Money`, `GeoCoordinate`, `FullGeoCoordinate`, `Range<T>`, `PreferredLocale`,
  `TimeUnit` — validate on construction so an existing instance is always valid.
- **Paging**: `IndexPage<T>`, `IndexPageRequest`, `ContinuationPage<T>`, `ContinuationPageRequest`, `PageMetadata`,
  `OrderBy`, `IHasOrderByRequest` / `IHasMultiOrderByRequest`.
- **Misc**: `ExtraProperties` / `IHasExtraProperties`, `Locales` / `LocaleAttribute`, `AsyncEvent<T>`, `NameValue` /
  `NameValue<T>`, `File` / `Image`, `TenantInformation`.

## Design Notes

This package was split out of `Headless.Extensions` so consumers can depend on the framework's value model
without pulling the full base library. `Headless.Extensions` keeps a `ProjectReference` to it, so every type here
remains transitively available to existing `Headless.Extensions` consumers.

`OrderBy(string Property, bool Ascending = true)` defaults to **ascending** — a model-bound or hand-written
`new OrderBy("Name")` sorts ascending, matching the near-universal convention. Pass `Ascending: false` for
descending.

`ExtraProperties` and `Locales` derive from `Dictionary<,>` (an established repo pattern for the
`IHasExtraProperties` bag); both are `sealed`.

## Installation

```bash
dotnet add package Headless.Primitives
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
```

### Money and MoneyAmount

```csharp
using Headless.Primitives;

var price = new Money(100m, "USD");
var withTax = price * 1.15m;                   // scalar scaling -> 115.00 USD (banker's rounding)
var total = price + new Money(20m, "USD");     // same-code addition; throws on code mismatch

var amount = new MoneyAmount(9.875m).GetRounded();   // 9.88 (MidpointRounding.ToEven)
```

### Ordering and paging

```csharp
using Headless.Primitives;

var order = new OrderBy("CreatedAt");              // ascending by default
var descending = new OrderBy("CreatedAt", Ascending: false);
```

## Configuration

None. Types are constructed directly; no DI registration or options are involved.

## Dependencies

- `Headless.Checks` - argument validation.
- `Headless.Generator.Primitives.Abstractions` - the `IPrimitive<T>` contract.
- `Headless.Generator.Primitives` - source generator (analyzer-only) for the built-in primitives.
- `libphonenumber-csharp` - phone-number parsing behind `PhoneNumber`.

## Side Effects

None. The types carry no DI registration or ambient state.
