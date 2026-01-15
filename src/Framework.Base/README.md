# Framework.Base

Foundational utility library providing extension methods, primitives, helpers, and common abstractions used throughout the framework.

## Problem Solved

Eliminates repetitive utility code across projects by providing a comprehensive set of battle-tested extensions, helpers, and primitives for common operations (strings, collections, dates, IO, reflection, etc.).

## Key Features

- **Result Pattern**: `Result<T>`, `DataResult<T>`, `NoDataResult` for exception-free control flow
- **Error Handling**: `ErrorDescriptor` for standardized error reporting
- **Domain Value Objects**: `GeoCoordinate`, `Currency`, `Range<T>`
- **Pagination**: `IndexPageRequest`/`ContinuationPageRequest` and response models
- **Collections**: `ForEachAsync`, `ParallelForEachAsync`, `Batch`, `DistinctBy`, and more
- **Dates & Time**: Fluent date manipulation, `TimeProvider` extensions, timezone conversion
- **Strings**: Humanize integration, manipulation helpers, high-performance toolkit
- **Reflection**: Fast property accessors, type scanning helpers
- **ID Generation**: `SnowflakeId`, `SequentialGuid`
- **Validation**: `MobilePhoneNumberValidator`, `GeoCoordinateValidator`, `EmailValidator`, `EgyptianNationalIdValidator`

## Installation

```bash
dotnet add package Framework.Base
```

## Quick Start

### Result Pattern

```csharp
public async Task<DataResult<User>> GetUserAsync(Guid id)
{
    var user = await _repo.GetByIdAsync(id);
    if (user is null)
        return DataResult<User>.Failure(new ErrorDescriptor("UserNotFound", "User does not exist"));

    return DataResult<User>.Success(user);
}
```

### Collection Extensions

```csharp
await users.ParallelForEachAsync(
    async user => await ProcessUserAsync(user),
    maxDegreeOfParallelism: 5
);
```

### Egyptian National ID Validator

```csharp
if (EgyptianNationalIdValidator.IsValid("29901011234567"))
{
    var info = EgyptianNationalIdValidator.Analyze("29901011234567");
    var birthDate = info.BirthDate;
    var governorate = info.Governorate;
}
```

## Configuration

No configuration required.

## Dependencies

- `Framework.Checks`
- `CommunityToolkit.HighPerformance`
- `Humanizer.Core`
- `IdGen`
- `libphonenumber-csharp`
- `morelinq`
- `Nito.AsyncEx`
- `Nito.Disposables`
- `Polly.Core`
- `TimeZoneConverter`

## Side Effects

None.
