// Copyright (c) Mahmoud Shaheen. All rights reserved.

using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Headless.Media.Indexing;

/// <summary>
/// Extracts plain text from a Word document (.docx) using the Open XML SDK.
/// </summary>
/// <remarks>
/// Text is collected from every <c>Paragraph</c> element in the main document body, preserving
/// paragraph boundaries as line breaks. Headers, footers, comments, text boxes, and embedded
/// objects are not included. The extraction is purely structural — OCR is not performed.
/// <para>
/// The Open XML SDK needs a seekable stream. A non-seekable input (a blob download, a request body) is first copied
/// into memory asynchronously, capped at 100 MiB and sized from the content length when the caller passes it; parsing
/// then runs synchronously with no further I/O.
/// </para>
/// </remarks>
[PublicAPI]
public sealed class WordDocumentMediaFileTextProvider : IMediaFileTextProvider
{
    /// <summary>
    /// Reads <paramref name="fileStream"/> and returns the paragraph text from the Word document body.
    /// </summary>
    /// <param name="fileStream">A stream containing a valid .docx (Open XML) Word document.</param>
    /// <param name="contentLength">
    /// The input's length in bytes when known (a blob download's <c>ContentLength</c>), used to size the buffer for a
    /// non-seekable stream and to refuse one over 100 MiB before reading it; <see langword="null"/> when unknown.
    /// </param>
    /// <param name="cancellationToken">
    /// Token to cancel the extraction. Observed while buffering a non-seekable stream, before parsing, and between
    /// paragraphs; the Open XML SDK parses the document synchronously, so cancellation cannot interrupt the parse of
    /// the package.
    /// </param>
    /// <returns>
    /// The plain-text content of all body paragraphs joined by line breaks, or an empty string when
    /// the document body contains no paragraphs.
    /// </returns>
    /// <exception cref="OperationCanceledException">
    /// Thrown when <paramref name="cancellationToken"/> is cancelled before extraction completes.
    /// </exception>
    /// <exception cref="System.Xml.XmlException">
    /// Thrown when a decompressed document part exceeds 16 Mi characters, as a zip bomb does.
    /// </exception>
    /// <exception cref="InvalidDataException">
    /// Thrown when a non-seekable <paramref name="fileStream"/> exceeds 100 MiB, or the extracted text exceeds 16 Mi
    /// characters.
    /// </exception>
    public async Task<string> GetTextAsync(
        Stream fileStream,
        long? contentLength = null,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        // The Open XML SDK copies a non-seekable stream synchronously and without a limit, so buffer it here instead.
        var buffered = await MediaTextLimits
            .BufferIfNotSeekableAsync(fileStream, contentLength, cancellationToken)
            .ConfigureAwait(false);

        await using (buffered)
        {
            return _ExtractText(buffered ?? fileStream, cancellationToken);
        }
    }

    private static string _ExtractText(Stream fileStream, CancellationToken cancellationToken)
    {
        using var document = WordprocessingDocument.Open(
            fileStream,
            isEditable: false,
            MediaTextLimits.OpenXmlSettings
        );

        var paragraphs = document.MainDocumentPart?.Document?.Body?.Descendants<Paragraph>().ToList();

        if (paragraphs is not { Count: > 0 })
        {
            return string.Empty;
        }

        var stringBuilder = new StringBuilder();

        foreach (var paragraph in paragraphs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            MediaTextLimits.AppendBounded(stringBuilder, paragraph.InnerText);
            stringBuilder.AppendLine();
        }

        return stringBuilder.ToString();
    }
}
