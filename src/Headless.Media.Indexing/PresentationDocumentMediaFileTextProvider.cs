// Copyright (c) Mahmoud Shaheen. All rights reserved.

using DocumentFormat.OpenXml.Drawing;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;

namespace Headless.Media.Indexing;

/// <summary>
/// Extracts plain text from a PowerPoint presentation (.pptx) using the Open XML SDK.
/// </summary>
/// <remarks>
/// Text is collected from every <c>Drawing.Text</c> element across all slides in slide-id order,
/// with paragraphs separated by spaces and slides separated by line breaks. Speaker notes,
/// embedded objects, charts, and SmartArt text are not included. The extraction is purely
/// structural — OCR is not performed.
/// <para>
/// The Open XML SDK needs a seekable stream. A non-seekable input (a blob download, a request body) is first copied
/// into memory asynchronously, capped at 100 MiB and sized from the content length when the caller passes it; parsing
/// then runs synchronously with no further I/O.
/// </para>
/// </remarks>
[PublicAPI]
public sealed class PresentationDocumentMediaFileTextProvider : IMediaFileTextProvider
{
    /// <summary>
    /// Reads <paramref name="fileStream"/> and returns the visible text from every slide in the
    /// presentation.
    /// </summary>
    /// <param name="fileStream">A stream containing a valid .pptx (Open XML) PowerPoint presentation.</param>
    /// <param name="contentLength">
    /// The input's length in bytes when known (a blob download's <c>ContentLength</c>), used to size the buffer for a
    /// non-seekable stream and to refuse one over 100 MiB before reading it; <see langword="null"/> when unknown.
    /// </param>
    /// <param name="cancellationToken">
    /// Token to cancel the extraction. Observed while buffering a non-seekable stream, before parsing, and between
    /// slides; the Open XML SDK parses the document synchronously, so cancellation cannot interrupt the parse of
    /// the package.
    /// </param>
    /// <returns>
    /// The plain-text content of all slides joined by line breaks, or an empty string when the
    /// presentation contains no slides or no extractable text.
    /// </returns>
    /// <exception cref="OperationCanceledException">
    /// Thrown when <paramref name="cancellationToken"/> is cancelled before extraction completes.
    /// </exception>
    /// <exception cref="System.Xml.XmlException">
    /// Thrown when a decompressed slide or presentation part exceeds 16 Mi characters, as a zip bomb does.
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
        using var document = PresentationDocument.Open(fileStream, isEditable: false, MediaTextLimits.OpenXmlSettings);
        var ids = document.PresentationPart?.Presentation?.SlideIdList?.ChildElements;

        if (ids is null || ids.Value.Count == 0)
        {
            return string.Empty;
        }

        var stringBuilder = new StringBuilder();

        foreach (var slideId in ids)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var relationshipId = ((SlideId)slideId).RelationshipId?.Value;

            if (
                relationshipId is null
                || document.PresentationPart?.GetPartById(relationshipId) is not SlidePart slidePart
            )
            {
                continue;
            }

            var slideText = _GetText(slidePart);

            if (!string.IsNullOrEmpty(slideText))
            {
                MediaTextLimits.AppendBounded(stringBuilder, slideText);
                stringBuilder.AppendLine();
            }
        }

        return stringBuilder.ToString();
    }

    private static string _GetText(SlidePart slidePart)
    {
        var stringBuilder = new StringBuilder();

        foreach (var paragraph in slidePart.Slide?.Descendants<Paragraph>() ?? [])
        {
            foreach (var text in paragraph.Descendants<DocumentFormat.OpenXml.Drawing.Text>())
            {
                stringBuilder.Append(text.Text);
            }

            stringBuilder.Append(' ');
        }

        return stringBuilder.ToString().Trim();
    }
}
