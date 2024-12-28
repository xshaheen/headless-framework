using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Framework.Checks;
using Framework.Media.Indexing;

namespace Tests;

public sealed class WordDocumentMediaFileTextProviderTests
{
    private readonly WordDocumentMediaFileTextProvider _sut = new();

    [Fact]
    public async Task get_text_async_should_extract_text_from_word_file()
    {
        // given
        var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
        var wordFilePath = Path.Combine(baseDirectory, @"..\..\..\Files\TestWORD.docx");
        await using var fileStream = File.OpenRead(wordFilePath);

        // when
        var result = await _sut.GetTextAsync(fileStream);

        // then
        result.Should().Contain("Lorem ipsum dolor"); // Replace with actual expected content
    }

    [Fact]
    public async Task get_text_async_should_return_empty_string_when_document_has_no_paragraphs()
    {
        // given
        await using var stream = _CreateEmptyWordDocument();

        // when
        var result = await _sut.GetTextAsync(stream);

        // then
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task get_text_async_should_return_paragraph_text_when_document_has_single_paragraph()
    {
        // given
        const string expectedText = "Test paragraph";
        await using var stream = _CreateWordDocumentWithText(expectedText);
        Argument.CanSeek(stream);
        Argument.CanRead(stream);
        Argument.CanWrite(stream);

        // when
        var result = await _sut.GetTextAsync(stream);

        // then
        result.Should().Be($"{expectedText}\r\n");
    }

    [Fact]
    public async Task get_text_async_should_return_all_paragraphs_when_document_has_multiple_paragraphs()
    {
        // given
        var paragraphs = new[] { "First paragraph", "Second paragraph", "Third paragraph" };
        await using var stream = _CreateWordDocumentWithParagraphs(paragraphs);
        var expectedText = string.Join("\r\n", paragraphs) + "\r\n";

        Argument.CanSeek(stream);
        Argument.CanRead(stream);
        Argument.CanWrite(stream);
        // when
        var result = await _sut.GetTextAsync(stream);

        // then
        result.Should().Be(expectedText);
    }

    private static MemoryStream _CreateEmptyWordDocument()
    {
        var stream = new MemoryStream();
        using var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document);
        document.AddMainDocumentPart();

        if (document.MainDocumentPart != null)
        {
            document.MainDocumentPart.Document = new Document(new Body());
        }

        document.Save();
        stream.Position = 0;

        return stream;
    }

    private static MemoryStream _CreateWordDocumentWithText(string text)
    {
        var stream = new MemoryStream();
        using var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document);
        var mainPart = document.AddMainDocumentPart();
        mainPart.Document = new Document(new Body(new Paragraph(new Run(new Text(text)))));
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
