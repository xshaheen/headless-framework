# Framework.Base

`Framework.Base` is the foundational utility library for the `Headless Framework`. It provides a rich set of primitives, extension methods, and core infrastructure patterns used throughout the ecosystem. It is designed to reduce boilerplate, standardize common operations (like error handling and pagination), and provide robust implementations for widely used types.

## Features

### Primitives & Patterns

-   **Result Pattern**: `Result<T>`, `DataResult<T>`, and `NoDataResult` types to handle operation outcomes without exceptions.
-   **Error Handling**: `ErrorDescriptor` for standardized error reporting (Code, Description, Severity).
-   **Domain Value Objects**:
    -   `GeoCoordinate` / `FullGeoCoordinate`: Location handling with distance calculations.
    -   `Currency`: type-safe currency handling.
    -   `Range<T>`: Generic range support.
-   **Pagination**: Standardized `IndexPageRequest`/`ContinuationPageRequest` and response models.

### Core Extensions

Extensive suite of extension methods to supercharge standard .NET types:

-   **Collections**: `ForEachAsync`, `ParallelForEachAsync`, `Batch`, `DistinctBy`, and more (built on top of `morelinq` and `System.Linq`).
-   **Dates & Time**: Fluent date manipulation, `TimeProvider` extensions, and timezone conversion.
-   **Strings**: `humanize` integration, string manipulation helpers, and HighPerformance toolkit integrations.
-   **Reflection**: Fast property accessors and type scanning helpers.

### Input Validation

Standalone validation logic (independent of FluentValidation) for specific domains:

-   **MobilePhoneNumberValidator**: Wrapper around `libphonenumber-csharp` for robust phone parsing and validation.
-   **GeoCoordinateValidator**: Latitude and Longitude bound checks.
-   **EmailValidator**: Regex-based email format validation.
-   **EgyptianNationalIdValidator**: Validates 14-digit Egyptian National IDs with birth date and governorate extraction.

### Utilities

-   **ID Generation**: `SnowflakeId` (Twitter Snowflake) and `SequentialGuid` generators.
-   **Async Helpers**: Integration with `Nito.AsyncEx` for robust async primitives (AsyncLock, AsyncLazy).
-   **Resilience**: `Polly` integration for retry and circuit breaker policies.
-   **Http**: `Flurl` integration for fluent HTTP requests.

## Dependencies

-   **Framework.Checks**: Guard clauses.
-   **CommunityToolkit.HighPerformance**: High-performance helpers.
-   **morelinq**: LINQ extensions.
-   **Flurl**: Fluent HTTP client.
-   **Humanizer.Core**: String manipulation.
-   **IdGen**: ID generation.
-   **libphonenumber-csharp**: Phone number parsing.
-   **Nito.AsyncEx**: Async coordination primitives.
-   **Polly.Core**: Resilience policies.

## Usage

### Result Pattern

Avoid exceptions for control flow by using the Result pattern.

```csharp
public async Task<DataResult<User>> GetUserAsync(Guid id)
{
    var user = await _repo.GetByIdAsync(id);
    if (user == null)
    {
        return DataResult<User>.Failure(new ErrorDescriptor("UserNotFound", "User does not exist"));
    }

    return DataResult<User>.Success(user);
}
```

### Egyptian National ID Validator

```csharp
bool isValid = EgyptianNationalIdValidator.IsValid("29901011234567");
if (isValid)
{
    var info = EgyptianNationalIdValidator.Analyze("29901011234567");
    var birthDate = info.BirthDate;
    var governorate = info.Governorate;
}
```

### Collections Extensions

```csharp
var users = await GetUsersAsync();

// Safe concurrent processing
await users.ParallelForEachAsync(async user => {
    await ProcessUserAsync(user);
}, maxDegreeOfParallelism: 5);
```
