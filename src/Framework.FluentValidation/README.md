# Framework.FluentValidation

`Framework.FluentValidation` is a comprehensive extension library for [FluentValidation](https://fluentvalidation.net/) that provides a suite of common, enterprise-grade validators and utilities. It includes specialized validation logic for phone numbers, national IDs, and standardized error handling.

## Features

-   **Advanced Phone Number Validation**: Powered by `libphonenumber-csharp` for robust international and country-specific phone number validation.
-   **Localized Validators**: Includes specific validators such as the Egyptian National ID validator.
-   **Property Path Normalization**: Automatically converts property paths to camelCase for consistent frontend consumption.

## Usage

### Phone Number Validation

The library provides robust phone number validation methods that handle parsing, country codes, and format checks.

```csharp
using Framework.FluentValidation;

public sealed class UserValidator : AbstractValidator<User>
{
    public UserValidator()
    {
        // Basic check using DataAnnotations
        RuleFor(x => x.PhoneNumber).BasicPhoneNumber();

        // Validate based on a dynamic country code from the instance
        RuleFor(x => x.PhoneNumber).PhoneNumber(user => user.CountryCode);

        // Validate as a fully qualified international number
        RuleFor(x => x.InternationalPhone).InternationalPhoneNumber();
    }
}
```

### Egyptian National ID Validation

Validate Egyptian National IDs with length checks and checksum verification (relies on `Framework.Validators`).

```csharp
using Framework.FluentValidation;

public sealed class CitizenValidator : AbstractValidator<Citizen>
{
    public CitizenValidator()
    {
        RuleFor(x => x.NationalId).EgyptianNationalId();
    }
}
```

### Error Descriptor Integration

Easily attach rich error descriptors to your validation rules, which is useful for returning structured error codes and messages from your API.

```csharp
using Framework.FluentValidation;
using Framework.Primitives;

public sealed class OrderValidator : AbstractValidator<Order>
{
    public OrderValidator()
    {
        RuleFor(x => x.Total).GreaterThan(0)
            .WithErrorDescriptor(new ErrorDescriptor("ORDER_TOTAL_INVALID", "Order total must be positive."));
    }
}
```

### Processing Validation Results

Convert FluentValidation results into a structured dictionary of error descriptors, suitable for API responses. This extension also handles camelCase normalization for property names.

```csharp
using FluentValidation;

public void ProcessValidation(ValidationResult result)
{
    if (!result.IsValid)
    {
        // Returns Dictionary<string, List<ErrorDescriptor>>
        var errors = result.Errors.ToErrorDescriptors();
    }
}
```

## Available Validators

-   **PhoneNumberValidators**: `BasicPhoneNumber`, `PhoneNumber`, `InternationalPhoneNumber`, `PhoneCountryCode`
-   **EgyptianNationalIdValidators**: `EgyptianNationalId`
-   **CollectionValidators**
    -   `MaximumElements(int maxElements)`
    -   `MinimumElements(int minElements)`
    -   `UniqueElements(IEqualityComparer<TElement>? comparer = null)`
    -   `UniqueElements(Func<TElement, TKey> keySelector, IEqualityComparer<TKey>? comparer = null)`
-   **GeoValidators**
    -   `Latitude()`: Validates latitude for `double`, `double?`, and `string`.
    -   `Longitude()`: Validates longitude for `double`, `double?`, and `string`.
-   **IdValidators**
    -   `Id()`: Validates that an identifier is valid (non-empty Guid, positive integer/long).
-   **PaginationValidators**
    -   `PageIndex()`: Ensures page index is 0 or greater.
    -   `PageSize(int maximumSize = 100)`: Ensures page size is positive and within limit.
    -   `SearchQuery(int maximumLength = 100)`: Limites search query length.
-   **UrlValidators**
    -   `Url()`: Validates absolute URLs.
    -   `HttpUrl()`: Validates HTTP/HTTPS URLs.
