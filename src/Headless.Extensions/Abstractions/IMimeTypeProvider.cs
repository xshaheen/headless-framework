// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Abstractions;

/// <summary>Used to look up MIME types given a file path</summary>
public interface IMimeTypeProvider
{
    /// <summary>
    /// Gets the MIME-type for the given file name, or fallback
    /// "application/octet-stream" if a mapping doesn't exist.
    /// </summary>
    /// <param name="fileName">A file path or sub path.</param>
    /// <returns>The MIME-type for the given file name.</returns>
    string GetMimeType(string fileName);

    /// <summary>Given a file path, determine the MIME type</summary>
    /// <param name="fileName">A file path or sub path.</param>
    /// <param name="contentType">The resulting MIME type</param>
    /// <returns>True if MIME type could be determined</returns>
    bool TryGetMimeType(string fileName, [NotNullWhen(true)] out string? contentType);

    /// <summary>Attempts to fetch all available file extensions for a MIME-type.</summary>
    /// <param name="mimeType">The name of the MIME-type</param>
    /// <returns>All available extensions for the given MIME-type</returns>
    IEnumerable<string> GetMimeTypeExtensions(string mimeType);
}

/// <summary>Default <see cref="IMimeTypeProvider"/> backed by the <c>MimeTypes</c> mapping table.</summary>
public sealed class MimeTypeProvider : IMimeTypeProvider
{
    /// <inheritdoc/>
    public string GetMimeType(string fileName)
    {
        return MimeTypes.GetMimeType(fileName);
    }

    /// <inheritdoc/>
    public bool TryGetMimeType(string fileName, [NotNullWhen(true)] out string? contentType)
    {
        return MimeTypes.TryGetMimeType(fileName, out contentType);
    }

    /// <inheritdoc/>
    public IEnumerable<string> GetMimeTypeExtensions(string mimeType)
    {
        return MimeTypes.GetMimeTypeExtensions(mimeType);
    }
}
