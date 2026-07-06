// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Media.Indexing;

/// <summary>
/// Selects the <see cref="IMediaFileTextProvider"/> that can extract text from a given file format.
/// </summary>
/// <remarks>
/// Consumers hold a stream plus a format hint (a file extension such as <c>pdf</c> / <c>.pdf</c>, or a
/// MIME type such as <c>application/pdf</c>) and need the matching provider without hard-coding the
/// format-to-provider mapping themselves. This resolver centralizes that dispatch.
/// </remarks>
public interface IMediaFileTextProviderResolver
{
    /// <summary>
    /// Returns the provider that handles <paramref name="fileExtensionOrMimeType"/>, or
    /// <see langword="null"/> when no registered provider supports the format.
    /// </summary>
    /// <param name="fileExtensionOrMimeType">
    /// A file extension (with or without a leading dot, case-insensitive) or a MIME type. For example
    /// <c>pdf</c>, <c>.pdf</c>, or <c>application/pdf</c>.
    /// </param>
    /// <returns>The matching <see cref="IMediaFileTextProvider"/>, or <see langword="null"/> when unsupported.</returns>
    /// <exception cref="ArgumentException"><paramref name="fileExtensionOrMimeType"/> is <see langword="null"/>, empty, or whitespace.</exception>
    IMediaFileTextProvider? GetProvider(string fileExtensionOrMimeType);
}
