// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace FluentValidation.Resources;

/// <summary>
/// Compile-time constants for the <c>errors[].code</c> values emitted by the file-upload FluentValidation rules
/// (see <see cref="FluentValidatorFormFileErrorDescriber"/>). All codes follow the framework-standard
/// <c>g:snake_case</c> shape (the <c>g:</c> prefix marks the shared "general" descriptor space; the <c>file_</c>
/// segment scopes the code to file-upload failures). Clients should branch on these constants rather than inspect
/// the human-readable description, which is localized.
/// </summary>
[PublicAPI]
public static class FluentValidatorFormFileErrorCodes
{
    /// <summary>A file that has zero bytes.</summary>
    public const string FileNotEmpty = "g:file_not_empty";

    /// <summary>A file whose size is below the required minimum.</summary>
    public const string FileGreaterThanOrEqualTo = "g:file_greater_than_or_equal_to";

    /// <summary>A file whose size exceeds the allowed maximum.</summary>
    public const string FileLessThanOrEqualTo = "g:file_less_than_or_equal_to";

    /// <summary>
    /// A file whose declared content type or binary signature does not match the expected format. Both
    /// content-type and signature failures share this code intentionally.
    /// </summary>
    public const string FileUnexpectedSignature = "g:file_unexpected_signature";
}
