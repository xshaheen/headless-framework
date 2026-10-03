// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Abstractions;

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
