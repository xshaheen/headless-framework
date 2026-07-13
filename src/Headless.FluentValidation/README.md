# Headless.FluentValidation

Extension library for FluentValidation providing additional validators and utilities.

## Problem Solved

Provides a comprehensive suite of common validators (phone numbers, national IDs, URLs, pagination) and standardized error handling, eliminating the need to rewrite common validation logic across projects.

## Key Features

- Phone number validation (international, country-specific, mobile-only) via `libphonenumber-csharp`
- Egyptian National ID validation with checksum verification
- Collection validators (unique elements, min/max counts)
- Geo validators (latitude/longitude)
- Pagination validators (page index, page size, search query)
- URL validators (absolute URLs, HTTP/HTTPS) and IP address validators (IPv4/IPv6)
- String-format validators (slug, username, ZIP code, hex color, decimal, Base64, trimmed, culture name)
- Relative date/time validators (`InThePast`/`InTheFuture`/`MinimumAge`, …) with an injectable `TimeProvider`
- Enum-name and markup-rejection (`NoScripts`) validators
- `ErrorDescriptor` integration for structured API responses
- Automatic camelCase property path normalization

## Installation

```bash
dotnet add package Headless.FluentValidation
```

## Quick Start

```csharp
using FluentValidation;

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

### Phone Number Validation

```csharp
RuleFor(x => x.Phone).BasicPhoneNumber(); // DataAnnotations check
RuleFor(x => x.Phone).PhoneNumber(u => u.CountryCode); // Country-specific
RuleFor(x => x.Phone).InternationalPhoneNumber(); // International format
RuleFor(x => x.Phone).MobilePhoneNumber(u => u.CountryCode); // Mobile-only (rejects landlines)
RuleFor(x => x.Phone).InternationalMobileNumber(); // E.164 mobile-only
```

### String, Network & Enum Validation

```csharp
RuleFor(x => x.Slug).Slug();
RuleFor(x => x.DisplayName).Trimmed();
RuleFor(x => x.Color).HexColor();          // "#1a2b3c" or "1A2B3C"
RuleFor(x => x.Payload).Base64();
RuleFor(x => x.Locale).Culture();          // "en", "en-US"
RuleFor(x => x.ServerIp).Ipv4();           // also Ipv6() / IpAddress()
RuleFor(x => x.Status).EnumName(typeof(OrderStatus)); // string must be a defined member name
RuleFor(x => x.Bio).NoScripts();           // reject <script> elements
```

### Date/Time Validation (testable via `TimeProvider`)

```csharp
RuleFor(x => x.PublishedAt).NotInThePast();          // DateTime/DateTimeOffset/DateOnly + nullable
RuleFor(x => x.ExpiresAt).InTheFuture();
RuleFor(x => x.BirthDate).MinimumAge(18);

// Pass a TimeProvider to make "now" deterministic in tests:
RuleFor(x => x.BirthDate).MinimumAge(18, fakeTimeProvider);
```

> `DateTime` values are compared in UTC (`Unspecified` kind is treated as UTC); `DateOnly` uses the current UTC date. `NotInThePast`/`NotInTheFuture` are inclusive of "now"; `InThePast`/`InTheFuture` are strict.

### Error Descriptor Integration

```csharp
RuleFor(x => x.Total)
    .GreaterThan(0)
    .WithErrorDescriptor(new ErrorDescriptor("ORDER_TOTAL_INVALID", "Total must be positive."));
```

`ErrorDescriptor` defaults to error severity. Pass `ValidationSeverity.Warning` or
`ValidationSeverity.Information` explicitly when defining a non-error rule; `WithErrorDescriptor(...)` carries
that severity into the FluentValidation failure.

### Processing Validation Results

```csharp
var errors = result.Errors.ToErrorDescriptors(); // IReadOnlyDictionary<string, IReadOnlyList<ErrorDescriptor>>
```

`ToErrorDescriptors()` preserves each failure's severity and normalizes its `ErrorCode` to the
framework-standard `g:snake_case` shape. FluentValidation built-in codes are mapped by
`FluentValidationErrorCodeMapper` (for example
`EmailValidator` → `g:invalid_email`), and the Headless validators in this package emit `g:`-prefixed
codes via `FluentValidatorErrorDescriber` (for example `Ipv4()` → `g:invalid_ipv4`). Clients therefore
see a single consistent code namespace in `errors[].code`. Codes you supply yourself through
`WithErrorDescriptor(...)` are passed through unchanged.

## Available Validators

| Category | Validators |
|----------|-----------|
| Phone | `BasicPhoneNumber`, `PhoneNumber`, `InternationalPhoneNumber`, `PhoneCountryCode`, `MobilePhoneNumber`, `InternationalMobileNumber` |
| National ID | `EgyptianNationalId` |
| Collection | `MaximumElements`, `MinimumElements`, `UniqueElements` |
| Geo | `Latitude`, `Longitude` (`double`, `double?`, and `string` overloads) |
| Pagination | `PageIndex`, `PageSize`, `SearchQuery` |
| URL | `Url`, `HttpUrl`, `HttpsOnlyUrl`, `FileUrl`, `FtpUrl`, `MailtoUrl`, `CorsOrigin` |
| Network | `Ipv4`, `Ipv6`, `IpAddress` |
| Storage Identifier | `IsValidPostgreSqlIdentifier`, `IsValidSqlServerIdentifier`, `IsValidCrossProviderIdentifier` |
| String | `OnlyIntegers`, `OnlyDecimals`, `Slug`, `Username`, `ZipCode`, `HexColor`, `Base64`, `Trimmed`, `Culture` |
| Date/Time | `InThePast`, `InTheFuture`, `NotInThePast`, `NotInTheFuture`, `MinimumAge` (`DateTime`/`DateTimeOffset`/`DateOnly` + nullable; `TimeProvider`-based) |
| Enum | `EnumName(typeof(TEnum))` |
| Safe Text | `NoScripts` |
| ID | `Id` (validates non-empty Guid, positive int/long) |

## Configuration

No configuration required.

## Dependencies

- `FluentValidation`
- `libphonenumber-csharp`
- `Headless.Extensions`

## Side Effects

None.
