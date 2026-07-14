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
    public void get_provider_should_resolve_pdf(string key)
    {
        _sut.GetProvider(key).Should().BeSameAs(_pdf);
    }

    [Theory]
    [InlineData("docx")]
    [InlineData(".DOCX")]
    [InlineData(ContentTypes.Applications.Docx)]
    public void get_provider_should_resolve_word(string key)
    {
        _sut.GetProvider(key).Should().BeSameAs(_word);
    }

    [Theory]
    [InlineData("pptx")]
    [InlineData(".pptx")]
    [InlineData(ContentTypes.Applications.PPtx)]
    public void get_provider_should_resolve_presentation(string key)
    {
        _sut.GetProvider(key).Should().BeSameAs(_presentation);
    }

    [Theory]
    [InlineData("txt")]
    [InlineData("application/octet-stream")]
    [InlineData(".xlsx")]
    public void get_provider_should_return_null_for_unsupported_format(string key)
    {
        _sut.GetProvider(key).Should().BeNull();
    }

    [Fact]
    public void get_provider_should_throw_for_blank_input()
    {
        var act = () => _sut.GetProvider("   ");

        act.Should().Throw<ArgumentException>();
    }
}
