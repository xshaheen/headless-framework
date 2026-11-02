using System.Xml;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Headless.Checks;
using Headless.Media.Indexing;
using Headless.Testing.Tests;

namespace Tests;

public sealed class WordDocumentMediaFileTextProviderTests : TestBase
{
    private readonly WordDocumentMediaFileTextProvider _sut = new();

    [Fact]
    public async Task should_extract_text_from_word_file_when_get_text_async()
    {
        // given
        var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
        var wordFilePath = Path.GetFullPath(Path.Combine(baseDirectory, "..", "..", "..", "Files", "TestWORD.docx"));
        await using var fileStream = File.OpenRead(wordFilePath);

        // when
        var result = await _sut.GetTextAsync(fileStream, AbortToken);

        // then
        result.Should().Contain("Lorem ipsum dolor"); // Replace with actual expected content
    }

    [Fact]
    public async Task should_return_empty_string_when_get_text_async_document_has_no_paragraphs()
    {
        // given
        await using var stream = _CreateEmptyWordDocument();

        // when
        var result = await _sut.GetTextAsync(stream, AbortToken);

        // then
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task should_return_all_paragraphs_when_get_text_async_document_has_multiple_paragraphs()
    {
        // given
        var paragraphs = new[] { "First paragraph", "Second paragraph", "Third paragraph" };
        await using var stream = _CreateWordDocumentWithParagraphs(paragraphs);
        var expectedText = string.Join(Environment.NewLine, paragraphs) + Environment.NewLine;

        Argument.CanSeek(stream);
        Argument.CanRead(stream);
        Argument.CanWrite(stream);
        // when
        var result = await _sut.GetTextAsync(stream, AbortToken);

        // then
        result.Should().Be(expectedText);
    }

    [Fact]
    public async Task should_refuse_a_document_part_that_inflates_past_the_part_limit()
    {
        // given: a body of 20 Mi repeated characters, which deflates to a few kilobytes, as a zip bomb does
        await using var stream = _CreateWordDocumentWithRawBodyText(20 * 1024 * 1024);

        // when
        var act = async () => await _sut.GetTextAsync(stream, AbortToken);

        // then
        await act.Should().ThrowAsync<XmlException>();
    }

    private static MemoryStream _CreateWordDocumentWithRawBodyText(int characters)
    {
        var stream = new MemoryStream();

        using (var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var mainPart = document.AddMainDocumentPart();

            // Stream the XML straight into the part so the test never holds the inflated document in memory.
            using var writer = new StreamWriter(mainPart.GetStream(FileMode.Create));
            writer.Write(
                "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body><w:p><w:r><w:t>"
            );
            var chunk = new string('A', 1024 * 1024);

            for (var written = 0; written < characters; written += chunk.Length)
            {
                writer.Write(chunk);
            }

            writer.Write("</w:t></w:r></w:p></w:body></w:document>");
        }

        stream.Position = 0;

        return stream;
    }

    private static MemoryStream _CreateEmptyWordDocument()
    {
        var stream = new MemoryStream();
        using var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document);
        document.AddMainDocumentPart();
        document.MainDocumentPart?.Document = new Document(new Body());
        document.Save();
        stream.Position = 0;

        return stream;
    }

    private static MemoryStream _CreateWordDocumentWithParagraphs(string[] paragraphs)
    {
        var stream = new MemoryStream();
        using var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document);
        var mainPart = document.AddMainDocumentPart();
        var body = new Body();

        foreach (var text in paragraphs)
        {
            body.AppendChild(new Paragraph(new Run(new Text(text))));
        }

        mainPart.Document = new Document(body);
        document.Save();
        stream.Position = 0;

        return stream;
    }
}
