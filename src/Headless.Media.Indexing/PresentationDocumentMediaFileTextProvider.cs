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
/// The implementation is synchronous internally and returns a completed <see cref="Task{TResult}"/>
/// via <c>Task.FromResult</c>; no I/O is performed after the initial read of the input stream.
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
    /// <param name="cancellationToken">
    /// Token to cancel the extraction. Observed before parsing and between slides; the Open XML SDK
    /// parses the document synchronously, so cancellation cannot interrupt the initial read.
    /// </param>
    /// <returns>
    /// The plain-text content of all slides joined by line breaks, or an empty string when the
    /// presentation contains no slides or no extractable text.
    /// </returns>
    /// <exception cref="OperationCanceledException">
    /// Thrown when <paramref name="cancellationToken"/> is cancelled before extraction completes.
    /// </exception>
    public Task<string> GetTextAsync(Stream fileStream, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var document = PresentationDocument.Open(fileStream, isEditable: false);
        var ids = document.PresentationPart?.Presentation?.SlideIdList?.ChildElements;

        if (ids is null || ids.Value.Count == 0)
        {
            return Task.FromResult(string.Empty);
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
                stringBuilder.AppendLine(slideText);
            }
        }

        return Task.FromResult(stringBuilder.ToString());
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
