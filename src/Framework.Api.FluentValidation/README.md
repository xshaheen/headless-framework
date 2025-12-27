# Framework.Api.FluentValidation

`Framework.Api.FluentValidation` extends the `Framework.FluentValidation` library to provide specialized validators for ASP.NET Core web APIs. It specifically focuses on validating `IFormFile` objects, making it easier to secure file uploads by validating file size, content type, and file signatures (magic numbers).

## Features

-   **IFormFile Validation**: Extension methods to validate `IFormFile` properties directly.
-   **Size Validation**: Validate minimum and maximum file sizes with formatted error messages.
-   **Content Type Validation**: Restrict uploads to specific MIME types.
-   **File Signature Validation**: Verify the actual file format using `FileSignatures` to prevent extension spoofing.

## Dependencies

-   .NET 10.0
-   [FileSignatures](https://www.nuget.org/packages/FileSignatures)
-   Framework.FluentValidation
-   Microsoft.AspNetCore.App

## Usage

### File Upload Validation

Validate uploaded files for size, content type, and actual file signature.

```csharp
using FluentValidation;
using Framework.FluentValidation;
using FileSignatures;
using FileSignatures.Formats;
using Microsoft.AspNetCore.Http;

public sealed class UploadProfileDirectoryRequestValidator : AbstractValidator<UploadProfileDirectoryRequest>
{
    public UploadProfileDirectoryRequestValidator(IFileFormatInspector fileInspector)
    {
        RuleFor(x => x.File)
            .FileNotEmpty()
            .LessThanOrEqualTo(5 * 1024 * 1024) // Max 5MB
            .ContentTypes(["image/jpeg", "image/png"])
            .HaveSignatures(fileInspector, format => format is Png or Jpeg);
    }
}
```

### Validator Methods

**FileValidators** provides the following extension methods for `IRuleBuilder<T, IFormFile?>`:

-   `FileNotEmpty()`: Ensures the file is not null and has a length greater than 0.
-   `GreaterThanOrEqualTo(int minBytes)`: Validates that the file size is at least `minBytes`. Error message includes formatted size (MB).
-   `LessThanOrEqualTo(int maxBytes)`: Validates that the file size does not exceed `maxBytes`. Error message includes formatted size (MB).
-   `ContentTypes(IReadOnlyList<string> contentTypes)`: Validates that the file's `ContentType` header matches one of the provided content types.
-   `HaveSignatures(IFileFormatInspector inspector, Func<FileFormat?, bool> predicate)`: Asynchronously inspects the file stream to determine its real format using `FileSignatures` and validates it against the provided predicate. This is crucial for security to ensure the file extension matches its content.
