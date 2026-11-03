// Copyright (c) Mahmoud Shaheen. All rights reserved.

using UglyToad.PdfPig;

namespace Headless.Media.Indexing;

/// <summary>
/// Extracts plain text from a PDF document using the PdfPig library.
/// </summary>
/// <remarks>
/// Text is read from each page's native text layer in document order. Scanned PDFs that contain
/// only rasterized images will return an empty string because OCR is not performed.
/// <para>
/// PdfPig requires a seekable stream. When the input stream is not seekable (for
/// example, a stream from Azure Blob Storage), the content is transparently buffered into a
/// <see cref="MemoryStream"/> before parsing, sized from the content length when the caller passes it. For large
/// documents this may consume significant memory, so a non-seekable stream is capped at 100 MiB; callers that already
/// hold seekable streams should prefer to pass them directly.
/// </para>
/// </remarks>
[PublicAPI]
public sealed class PdfMediaFileTextProvider : IMediaFileTextProvider
{
    /// <summary>
    /// Reads <paramref name="fileStream"/> and returns the concatenated text from every page of the PDF.
    /// </summary>
    /// <param name="fileStream">
    /// A stream containing a valid PDF document. May be non-seekable; in that case the content is
    /// buffered into memory automatically before parsing.
    /// </param>
    /// <param name="contentLength">
    /// The input's length in bytes when known (a blob download's <c>ContentLength</c>), used to size the buffer for a
    /// non-seekable stream and to refuse one over 100 MiB before reading it; <see langword="null"/> when unknown.
    /// </param>
    /// <param name="cancellationToken">
    /// Token to cancel the extraction. Observed while buffering a non-seekable stream and between pages;
    /// PdfPig parses each page synchronously, so cancellation cannot interrupt a single-page parse.
    /// </param>
    /// <returns>
    /// The plain-text content of all pages concatenated in page order, or an empty string when the
    /// document contains no extractable text.
    /// </returns>
    /// <exception cref="OperationCanceledException">
    /// Thrown when <paramref name="cancellationToken"/> is cancelled before extraction completes.
    /// </exception>
    /// <exception cref="InvalidDataException">
    /// Thrown when a non-seekable <paramref name="fileStream"/> exceeds 100 MiB, or the extracted text exceeds
    /// 16 Mi characters.
    /// </exception>
    public async Task<string> GetTextAsync(
        Stream fileStream,
        long? contentLength = null,
        CancellationToken cancellationToken = default
    )
    {
        // PdfPig requires the stream to be seekable, see:
        // https://github.com/UglyToad/PdfPig/blob/master/src/UglyToad.PdfPig.Core/StreamInputBytes.cs#L45.
        // Thus, if it isn't, which is the case with e.g. Azure Blob Storage, it is buffered first.
        var buffered = await MediaTextLimits
            .BufferIfNotSeekableAsync(fileStream, contentLength, cancellationToken)
            .ConfigureAwait(false);

        try
        {
            using var document = PdfDocument.Open(buffered ?? fileStream);
            var stringBuilder = new StringBuilder();

            foreach (var page in document.GetPages())
            {
                cancellationToken.ThrowIfCancellationRequested();
                MediaTextLimits.AppendBounded(stringBuilder, page.Text);
            }

            return stringBuilder.ToString();
        }
        finally
        {
            if (buffered is not null)
            {
                await buffered.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
