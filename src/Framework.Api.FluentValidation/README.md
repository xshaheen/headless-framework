# Framework.Api.FluentValidation

FluentValidation extensions for validating ASP.NET Core `IFormFile` uploads including size, content type, and file signature verification.

## Problem Solved

Provides reusable, type-safe validators for file uploads with proper error messages, eliminating boilerplate validation code for common file upload scenarios and preventing extension spoofing attacks.

## Key Features

- `FileNotEmpty()` - Validates file has content
- `GreaterThanOrEqualTo(bytes)` - Minimum file size validation
- `LessThanOrEqualTo(bytes)` - Maximum file size validation
- `ContentTypes(list)` - MIME type whitelist validation
- `HaveSignatures(inspector, predicate)` - Magic bytes/file signature validation
- Localized error messages (English, Arabic)

## Installation

```bash
dotnet add package Framework.Api.FluentValidation
```

## Quick Start

```csharp
using FluentValidation;
using Framework.FluentValidation;
using FileSignatures;
using FileSignatures.Formats;

public sealed class UploadRequestValidator : AbstractValidator<UploadRequest>
{
    public UploadRequestValidator(IFileFormatInspector inspector)
    {
        RuleFor(x => x.Avatar)
            .FileNotEmpty()
            .LessThanOrEqualTo(5 * 1024 * 1024) // 5MB
            .ContentTypes(["image/jpeg", "image/png"])
            .HaveSignatures(inspector, format => format is Jpeg or Png);
    }
}
```

## Configuration

No configuration required.

## Dependencies

- `Framework.FluentValidation`
- `FileSignatures`
- `Microsoft.AspNetCore.App` (framework reference)

## Side Effects

None.
