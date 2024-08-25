using Cysharp.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Framework.Media.Indexing;

public sealed class WordDocumentMediaFileTextProvider : IMediaFileTextProvider
{
    public Task<string> GetTextAsync(string path, Stream fileStream)
    {
        using var document = WordprocessingDocument.Open(fileStream, false);

        var paragraphs = document.MainDocumentPart?.Document.Body?.Descendants<Paragraph>();

        if (paragraphs is null || !paragraphs.Any())
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
