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
/// memory; callers that already hold seekable streams should prefer to pass them directly.
/// </para>
/// </remarks>
public sealed class PdfMediaFileTextProvider : IMediaFileTextProvider
{
    /// <summary>
    /// Reads <paramref name="fileStream"/> and returns the concatenated text from every page of the PDF.
    /// </summary>
    /// <param name="fileStream">
    /// A stream containing a valid PDF document. May be non-seekable; in that case the content is
    /// buffered into memory automatically before parsing.
    /// </param>
    /// <returns>
    /// The plain-text content of all pages concatenated in page order, or an empty string when the
    /// document contains no extractable text.
    /// </returns>
    public async Task<string> GetTextAsync(Stream fileStream)
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
                // MemoryStream.
                seekableStream = new MemoryStream();
                // While this involves loading the file into memory, we don't really have a choice.
                await fileStream.CopyToAsync(seekableStream);
                seekableStream.Position = 0;
            }

            using var document = PdfDocument.Open(seekableStream ?? fileStream);
            var stringBuilder = new StringBuilder();

            foreach (var page in document.GetPages())
            {
                stringBuilder.Append(page.Text);
            }

            return stringBuilder.ToString();
        }
        finally
        {
            if (seekableStream is not null)
            {
                await seekableStream.DisposeAsync();
            }
        }
    }
}
