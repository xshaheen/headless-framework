// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Cysharp.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Framework.Media.Indexing;

public sealed class WordDocumentMediaFileTextProvider : IMediaFileTextProvider
{
    public Task<string> GetTextAsync(string path, Stream fileStream)
    {
        using var document = WordprocessingDocument.Open(fileStream, isEditable: false);

        var paragraphs = document.MainDocumentPart?.Document.Body?.Descendants<Paragraph>();

        if (paragraphs?.Any() != true)
        {
            return Task.FromResult(string.Empty);
        }

        using var stringBuilder = ZString.CreateStringBuilder();

        foreach (var paragraph in paragraphs)
        {
            stringBuilder.AppendLine(paragraph.InnerText);
        }

        return Task.FromResult(stringBuilder.ToString());
    }
}
