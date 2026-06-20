// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Abstractions;

/// <summary>Looks up MIME types by file name and, conversely, file extensions by MIME type.</summary>
public interface IMimeTypeProvider
{
    /// <summary>Returns the MIME type for the given file name, or <c>application/octet-stream</c> when no mapping exists.</summary>
    /// <param name="fileName">A file name or path whose extension is used for the lookup.</param>
    /// <returns>The MIME type string for <paramref name="fileName"/>.</returns>
    string GetMimeType(string fileName);

    /// <summary>Attempts to determine the MIME type for the given file name.</summary>
    /// <param name="fileName">A file name or path whose extension is used for the lookup.</param>
    /// <param name="contentType">When this method returns <see langword="true"/>, the MIME type for <paramref name="fileName"/>; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when a mapping was found; otherwise <see langword="false"/>.</returns>
    bool TryGetMimeType(string fileName, [NotNullWhen(true)] out string? contentType);

    /// <summary>Returns all known file extensions registered for the given MIME type.</summary>
    /// <param name="mimeType">The MIME type to look up extensions for.</param>
    /// <returns>The set of file extensions (including the leading dot) associated with <paramref name="mimeType"/>; empty when none are registered.</returns>
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
