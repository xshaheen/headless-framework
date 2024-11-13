// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Cysharp.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Framework.Media.Indexing;

public sealed class PresentationDocumentMediaFileTextProvider : IMediaFileTextProvider
{
    public Task<string> GetTextAsync(string path, Stream fileStream)
    {
        using var document = PresentationDocument.Open(fileStream, isEditable: false);
        var slideIds = document.PresentationPart?.Presentation.SlideIdList?.ChildElements.Cast<SlideId>();

        if (slideIds?.Any() != true)
        {
            return Task.FromResult(string.Empty);
        }

        using var stringBuilder = ZString.CreateStringBuilder();

        foreach (var slideId in slideIds)
        {
            var relationshipId = slideId.RelationshipId?.Value;

            if (
                relationshipId is null
                || document.PresentationPart!.GetPartById(relationshipId) is not SlidePart slidePart
            )
            {
                continue;
            }

            var slideText = _GetText(slidePart);

            stringBuilder.AppendLine(slideText);
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
        }

        return stringBuilder.ToString();
    }
}
