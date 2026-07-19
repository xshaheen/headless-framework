// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;
using FluentValidatorErrors = Headless.FluentValidation.Resources.FluentValidatorErrors;

namespace Headless.FluentValidation.Resources;

/// <summary>
/// Factory methods that produce <see cref="ErrorDescriptor"/> instances for the file-upload FluentValidation rules
/// defined in <see cref="global::FluentValidation.HeadlessFileValidators"/>.
/// </summary>
/// <remarks>
/// Each method returns a new <see cref="ErrorDescriptor"/> whose code follows the framework-standard
/// <c>g:snake_case</c> shape (the <c>g:</c> prefix marks the shared "general" descriptor space; the
/// <c>file_</c> segment scopes the code to file-upload failures) and a localised description sourced from
/// <c>FluentValidatorErrors</c> resource strings. These descriptors are consumed by the
/// <c>.WithErrorDescriptor()</c> FluentValidation extension to populate the <c>code</c> and <c>description</c>
/// fields on validation failures.
/// </remarks>
[PublicAPI]
public static class FluentValidatorFormFileErrorDescriber
{
    /// <summary>Returns the error descriptor for a file that has zero bytes.</summary>
    /// <returns>An <see cref="ErrorDescriptor"/> with code <c>g:file_not_empty</c>.</returns>
    public static ErrorDescriptor FileNotEmpty()
    {
        return new(
            code: FluentValidatorFormFileErrorCodes.FileNotEmpty,
            description: FluentValidatorErrors.g_file_not_empty
        );
    }

    /// <summary>Returns the error descriptor for a file whose size is below the required minimum.</summary>
    /// <returns>An <see cref="ErrorDescriptor"/> with code <c>g:file_greater_than_or_equal_to</c>.</returns>
    public static ErrorDescriptor FileGreaterThanOrEqualToValidator()
    {
        return new(
            code: FluentValidatorFormFileErrorCodes.FileGreaterThanOrEqualTo,
            description: FluentValidatorErrors.g_file_greater_than_or_equal_to
        );
    }

    /// <summary>Returns the error descriptor for a file whose size exceeds the allowed maximum.</summary>
    /// <returns>An <see cref="ErrorDescriptor"/> with code <c>g:file_less_than_or_equal_to</c>.</returns>
    public static ErrorDescriptor FileLessThanOrEqualToValidator()
    {
        return new(
            code: FluentValidatorFormFileErrorCodes.FileLessThanOrEqualTo,
            description: FluentValidatorErrors.g_file_less_than_or_equal_to
        );
    }

    /// <summary>Returns the error descriptor for a file whose declared content type is not in the allowed list.</summary>
    /// <returns>An <see cref="ErrorDescriptor"/> with code <c>g:file_unexpected_signature</c>. Both content-type and signature failures share this code intentionally.</returns>
    public static ErrorDescriptor FileContentTypeValidator()
    {
        return new(
            code: FluentValidatorFormFileErrorCodes.FileUnexpectedSignature,
            description: FluentValidatorErrors.g_file_unexpected_signature
        );
    }

    /// <summary>Returns the error descriptor for a file whose binary signature does not match the expected format.</summary>
    /// <returns>An <see cref="ErrorDescriptor"/> with code <c>g:file_unexpected_signature</c>.</returns>
    public static ErrorDescriptor FileSignatureValidator()
    {
        return new(
            code: FluentValidatorFormFileErrorCodes.FileUnexpectedSignature,
            description: FluentValidatorErrors.g_file_unexpected_signature
        );
    }
}
