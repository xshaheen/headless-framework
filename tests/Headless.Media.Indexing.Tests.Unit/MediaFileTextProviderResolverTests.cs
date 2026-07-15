using Headless.Constants;
using Headless.Media.Indexing;

namespace Tests;

public sealed class MediaFileTextProviderResolverTests
{
    private readonly PdfMediaFileTextProvider _pdf = new();
    private readonly WordDocumentMediaFileTextProvider _word = new();
    private readonly PresentationDocumentMediaFileTextProvider _presentation = new();
    private readonly MediaFileTextProviderResolver _sut;

    public MediaFileTextProviderResolverTests()
    {
        _sut = new MediaFileTextProviderResolver(_pdf, _word, _presentation);
    }

    [Theory]
    [InlineData("pdf")]
    [InlineData(".pdf")]
    [InlineData("PDF")]
    [InlineData(ContentTypes.Applications.Pdf)]
    public void should_resolve_pdf_when_get_provider(string key)
    {
        _sut.GetProvider(key).Should().BeSameAs(_pdf);
    }

    [Theory]
    [InlineData("docx")]
    [InlineData(".DOCX")]
    [InlineData(ContentTypes.Applications.Docx)]
    public void should_resolve_word_when_get_provider(string key)
    {
        _sut.GetProvider(key).Should().BeSameAs(_word);
    }

    [Theory]
    [InlineData("pptx")]
    [InlineData(".pptx")]
    [InlineData(ContentTypes.Applications.PPtx)]
    public void should_resolve_presentation_when_get_provider(string key)
    {
        _sut.GetProvider(key).Should().BeSameAs(_presentation);
    }

    [Theory]
    [InlineData("txt")]
    [InlineData("application/octet-stream")]
    [InlineData(".xlsx")]
    public void should_return_null_for_unsupported_format_when_get_provider(string key)
    {
        _sut.GetProvider(key).Should().BeNull();
    }

    [Fact]
    public void should_throw_for_blank_input_when_get_provider()
    {
        var act = () => _sut.GetProvider("   ");

        act.Should().Throw<ArgumentException>();
    }
}
