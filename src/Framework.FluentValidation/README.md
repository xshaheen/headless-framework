# Framework.FluentValidation

Extension library for FluentValidation providing enterprise-grade validators and utilities.

## Problem Solved

Provides a comprehensive suite of common validators (phone numbers, national IDs, URLs, pagination) and standardized error handling, eliminating the need to rewrite common validation logic across projects.

## Key Features

- Phone number validation (international, country-specific) via `libphonenumber-csharp`
- Egyptian National ID validation with checksum verification
- Collection validators (unique elements, min/max counts)
- Geo validators (latitude/longitude)
- Pagination validators (page index, page size, search query)
- URL validators (absolute URLs, HTTP/HTTPS)
- `ErrorDescriptor` integration for structured API responses
- Automatic camelCase property path normalization

## Installation

```bash
dotnet add package Framework.FluentValidation
```

## Quick Start

```csharp
using Framework.FluentValidation;

public sealed class UserValidator : AbstractValidator<User>
{
    public UserValidator()
    {
        RuleFor(x => x.PhoneNumber).InternationalPhoneNumber();
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Roles).MinimumElements(1).UniqueElements();
    }
}
```

## Usage

### Phone Number Validation

```csharp
RuleFor(x => x.Phone).BasicPhoneNumber();                    // DataAnnotations check
RuleFor(x => x.Phone).PhoneNumber(u => u.CountryCode);       // Country-specific
RuleFor(x => x.Phone).InternationalPhoneNumber();            // International format
```

### Error Descriptor Integration

```csharp
RuleFor(x => x.Total).GreaterThan(0)
    .WithErrorDescriptor(new ErrorDescriptor("ORDER_TOTAL_INVALID", "Total must be positive."));
```

### Processing Validation Results

```csharp
var errors = result.Errors.ToErrorDescriptors(); // Dictionary<string, List<ErrorDescriptor>>
```

## Available Validators

| Category | Validators |
|----------|-----------|
| Phone | `BasicPhoneNumber`, `PhoneNumber`, `InternationalPhoneNumber`, `PhoneCountryCode` |
| National ID | `EgyptianNationalId` |
| Collection | `MaximumElements`, `MinimumElements`, `UniqueElements` |
| Geo | `Latitude`, `Longitude` |
| Pagination | `PageIndex`, `PageSize`, `SearchQuery` |
| URL | `Url`, `HttpUrl` |
| ID | `Id` (validates non-empty Guid, positive int/long) |

## Configuration

No configuration required.

## Dependencies

- `FluentValidation`
- `libphonenumber-csharp`
- `Framework.Base`

## Side Effects

None.
