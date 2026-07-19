# Headless.Api.FluentValidation

FluentValidation extensions for ASP.NET Core file uploads and reusable Headless API request contracts.

## Problem Solved

Provides reusable, type-safe validators for file uploads and common API request contracts, keeping validation rules out of `Headless.Api.Core` while eliminating repeated boundary-validation boilerplate.

## Key Features

- `FileNotEmpty()` - Validates file has content
- `GreaterThanOrEqualTo(bytes)` - Minimum file size validation
- `LessThanOrEqualTo(bytes)` - Maximum file size validation
- `ContentTypes(list)` - MIME type whitelist validation
- `HaveSignatures(inspector, predicate)` - Magic bytes/file signature validation
- `PhoneNumber()` - Validates `PhoneNumberRequest` country code and local subscriber number
- `GeoCoordinate()` - Validates `GeoCoordinateRequest` latitude/longitude ranges
- `PageMetadata()` - Validates `PageMetadataRequest` SEO field length and element-count limits
- Localized error messages (English, Arabic)

## Installation

```bash
dotnet add package Headless.Api.FluentValidation
```

## Quick Start

```csharp
using FileSignatures;
using FileSignatures.Formats;
using FluentValidation;
using Headless.Api.Contracts;
using Microsoft.AspNetCore.Http;

public sealed record ProfileRequest(
    IFormFile? Avatar,
    PhoneNumberRequest? PhoneNumber,
    GeoCoordinateRequest? Location,
    PageMetadataRequest? Metadata
);

public sealed class ProfileRequestValidator : AbstractValidator<ProfileRequest>
{
    public ProfileRequestValidator(IFileFormatInspector inspector)
    {
        RuleFor(x => x.Avatar)
            .FileNotEmpty()
            .LessThanOrEqualTo(5 * 1024 * 1024) // 5MB
            .ContentTypes(["image/jpeg", "image/png"])
            .HaveSignatures(inspector, format => format is Jpeg or Png);

        RuleFor(x => x.PhoneNumber).PhoneNumber();
        RuleFor(x => x.Location).GeoCoordinate();
        RuleFor(x => x.Metadata).PageMetadata();
    }
}
```

## Configuration

No configuration required.

File-validation extensions remain in the `FluentValidation` namespace. Stable file error-code constants and
localized descriptor factories live in `Headless.FluentValidation.Resources`.

## Dependencies

- `Headless.FluentValidation`
- `Headless.Api.Core`
- `FileSignatures`
- `Microsoft.AspNetCore.App` (framework reference)

## Side Effects

None.
