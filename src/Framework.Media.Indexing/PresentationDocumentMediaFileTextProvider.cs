// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Cysharp.Text;
using DocumentFormat.OpenXml.Drawing;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;

namespace Framework.Media.Indexing;

public sealed class PresentationDocumentMediaFileTextProvider : IMediaFileTextProvider
{
    public Task<string> GetTextAsync(Stream fileStream)
    {
        using var document = PresentationDocument.Open(fileStream, isEditable: false);
        var ids = document.PresentationPart?.Presentation.SlideIdList?.ChildElements;

        if (ids is null || ids.Value.Count == 0)
        {
            return Task.FromResult(string.Empty);
        }

        using var stringBuilder = ZString.CreateStringBuilder();

        foreach (var slideId in ids)
        {
            var relationshipId = ((SlideId) slideId).RelationshipId?.Value;

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
        using var stringBuilder = ZString.CreateStringBuilder();

        foreach (var paragraph in slidePart.Slide.Descendants<Paragraph>())
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
