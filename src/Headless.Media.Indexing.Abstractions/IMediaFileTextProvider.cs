// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Media.Indexing;

/// <summary>
/// Extracts the plain-text content of a media file for indexing or search purposes.
/// </summary>
/// <remarks>
/// Each implementation targets a specific file format (PDF, Word, PowerPoint, etc.). The caller is
/// responsible for selecting the appropriate provider for a given MIME type or file extension —
/// this interface makes no attempt to detect or validate the file format itself.
/// <para>
/// Text is extracted from the document's structure; optical character recognition (OCR) is not
/// performed. Images, charts, and other non-text content are silently ignored.
/// </para>
/// </remarks>
public interface IMediaFileTextProvider
{
    /// <summary>
    /// Reads <paramref name="fileStream"/> and returns the plain-text content of the file.
    /// </summary>
    /// <param name="fileStream">
    /// A stream whose content is the media file to extract text from. Whether the stream must be
    /// seekable depends on the implementation — see the concrete provider's documentation.
    /// </param>
    /// <param name="cancellationToken">Token to cancel the extraction.</param>
    /// <returns>
    /// The extracted plain-text content of the file, or an empty string when no text could be found.
    /// </returns>
    /// <exception cref="OperationCanceledException">
    /// Thrown when <paramref name="cancellationToken"/> is cancelled before extraction completes.
    /// </exception>
    Task<string> GetTextAsync(Stream fileStream, CancellationToken cancellationToken = default);
}
