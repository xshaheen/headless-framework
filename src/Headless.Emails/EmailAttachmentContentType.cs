// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Emails;

/// <summary>
/// Resolves an attachment's MIME content type from its file name. Shared across email providers whose
/// transport requires an explicit content type (for example Azure Communication Services), since the
/// <see cref="EmailRequestAttachment"/> contract carries only the file name and bytes.
/// </summary>
[PublicAPI]
public static class EmailAttachmentContentType
{
    /// <summary>The fallback content type used when the file name has no recognized extension.</summary>
    public const string Default = "application/octet-stream";

    /// <summary>
    /// Resolves the MIME content type for <paramref name="fileName"/> from its extension, falling back
    /// to <see cref="Default"/> (<c>application/octet-stream</c>) when the extension is unknown.
    /// </summary>
    /// <param name="fileName">The attachment file name (for example <c>invoice.pdf</c>).</param>
    /// <returns>The resolved MIME content type, never <see langword="null"/> or empty.</returns>
    /// <exception cref="System.ArgumentException">Thrown when <paramref name="fileName"/> is <see langword="null"/> or empty.</exception>
    public static string Resolve(string fileName)
    {
        Argument.IsNotNullOrEmpty(fileName);

        var contentType = MimeTypes.GetMimeType(fileName);

        return string.IsNullOrEmpty(contentType) ? Default : contentType;
    }
}
