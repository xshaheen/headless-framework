// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Emails;

/// <summary>
/// Resolves the MIME content type of an email attachment from its file name. Shared across email providers
/// whose transport requires an explicit content type, such as Azure Communication Services, because the
/// <see cref="EmailRequestAttachment"/> contract carries only the file name and bytes.
/// </summary>
[PublicAPI]
public static class EmailAttachmentContentType
{
    /// <summary>The fallback content type used when the file name has no recognized extension.</summary>
    public const string Default = "application/octet-stream";

    /// <summary>
    /// Resolves the MIME content type for a file name from its extension.
    /// </summary>
    /// <param name="fileName">The attachment file name, for example <c>invoice.pdf</c>.</param>
    /// <returns>
    /// The resolved MIME content type, never <see langword="null"/> or empty: an unrecognized extension
    /// falls back to <see cref="Default"/> (<c>application/octet-stream</c>).
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="fileName"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="fileName"/> is empty.</exception>
    public static string Resolve(string fileName)
    {
        Argument.IsNotNullOrEmpty(fileName);

        var contentType = MimeTypes.GetMimeType(fileName);

        return string.IsNullOrEmpty(contentType) ? Default : contentType;
    }
}
