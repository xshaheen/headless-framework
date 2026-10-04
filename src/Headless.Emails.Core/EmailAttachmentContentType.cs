// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Emails;

/// <summary>
/// Resolves MIME content types for email attachments from file names.
/// </summary>
[PublicAPI]
public static class EmailAttachmentContentType
{
    /// <summary>The fallback content type used when the file name has no recognized extension.</summary>
    public const string Default = "application/octet-stream";

    /// <summary>
    /// Resolves the MIME content type for a file name from its extension.
    /// </summary>
    /// <param name="fileName">The attachment file name.</param>
    /// <returns>The resolved MIME content type, or <see cref="Default"/> when the extension is unrecognized.</returns>
    /// <exception cref="ArgumentException"><paramref name="fileName"/> is <see langword="null"/> or empty.</exception>
    public static string Resolve(string fileName)
    {
        Argument.IsNotNullOrEmpty(fileName);

        var contentType = MimeTypes.GetMimeType(fileName);

        return string.IsNullOrEmpty(contentType) ? Default : contentType;
    }
}
