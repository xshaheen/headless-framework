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
/// <see cref="MemoryStream"/> before parsing. For large documents this may consume significant
/// memory, so a non-seekable stream is capped at 100 MiB; callers that already hold seekable streams should prefer to
/// pass them directly.
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
    public async Task<string> GetTextAsync(Stream fileStream, CancellationToken cancellationToken = default)
    {
        // PdfPig requires the stream to be seekable, see:
        // https://github.com/UglyToad/PdfPig/blob/master/src/UglyToad.PdfPig.Core/StreamInputBytes.cs#L45.
        // Thus, if it isn't, which is the case with e.g. Azure Blob Storage, we need to copy it to a new, seekable
        // Stream.
        MemoryStream? seekableStream = null;

        try
        {
            if (!fileStream.CanSeek)
            {
                // Since fileStream.Length might not be supported either, we can't preconfigure the capacity of the
                // MemoryStream. Buffering is the only option, so it is capped: an unbounded body would otherwise be
                // read into memory whole.
                seekableStream = new MemoryStream();
                await _CopyBoundedAsync(fileStream, seekableStream, cancellationToken).ConfigureAwait(false);
                seekableStream.Position = 0;
            }

            using var document = PdfDocument.Open(seekableStream ?? fileStream);
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
            if (seekableStream is not null)
            {
                await seekableStream.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private static async Task _CopyBoundedAsync(
        Stream source,
        MemoryStream destination,
        CancellationToken cancellationToken
    )
    {
        var buffer = new byte[81920];
        int read;

        while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (destination.Length + read > MediaTextLimits.MaxBufferedPdfBytes)
            {
                throw new InvalidDataException(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"The non-seekable PDF stream exceeds {MediaTextLimits.MaxBufferedPdfBytes} bytes, the most the provider buffers. Pass a seekable stream instead."
                    )
                );
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }
    }
}
