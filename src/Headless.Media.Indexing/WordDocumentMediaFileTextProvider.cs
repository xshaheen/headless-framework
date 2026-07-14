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
/// The implementation is synchronous internally and returns a completed <see cref="Task{TResult}"/>
/// via <c>Task.FromResult</c>; no I/O is performed after the initial read of the input stream.
/// </para>
/// </remarks>
[PublicAPI]
public sealed class WordDocumentMediaFileTextProvider : IMediaFileTextProvider
{
    /// <summary>
    /// Reads <paramref name="fileStream"/> and returns the paragraph text from the Word document body.
    /// </summary>
    /// <param name="fileStream">A stream containing a valid .docx (Open XML) Word document.</param>
    /// <param name="cancellationToken">
    /// Token to cancel the extraction. Observed before parsing and between paragraphs; the Open XML SDK
    /// parses the document synchronously, so cancellation cannot interrupt the initial read.
    /// </param>
    /// <returns>
    /// The plain-text content of all body paragraphs joined by line breaks, or an empty string when
    /// the document body contains no paragraphs.
    /// </returns>
    /// <exception cref="OperationCanceledException">
    /// Thrown when <paramref name="cancellationToken"/> is cancelled before extraction completes.
    /// </exception>
    public Task<string> GetTextAsync(Stream fileStream, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var document = WordprocessingDocument.Open(fileStream, isEditable: false);

        var paragraphs = document.MainDocumentPart?.Document?.Body?.Descendants<Paragraph>().ToList();

        if (paragraphs is not { Count: > 0 })
        {
            return Task.FromResult(string.Empty);
        }

        var stringBuilder = new StringBuilder();

        foreach (var paragraph in paragraphs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            stringBuilder.AppendLine(paragraph.InnerText);
        }

        return Task.FromResult(stringBuilder.ToString());
    }
}
