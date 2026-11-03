using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Drawing;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using Headless.IO;
using Headless.Media.Indexing;
using Headless.Testing.Tests;
using Path = System.IO.Path;
using Shape = DocumentFormat.OpenXml.Presentation.Shape;
using Text = DocumentFormat.OpenXml.Drawing.Text;
using TextBody = DocumentFormat.OpenXml.Presentation.TextBody;

namespace Tests;

public sealed class PresentationDocumentMediaFileTextProviderTests : TestBase
{
    private readonly PresentationDocumentMediaFileTextProvider _sut = new();

    [Fact]
    public async Task should_extract_text_from_power_point_file_when_get_text_async()
    {
        // given
        var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
        var powerPintFilePath = Path.GetFullPath(
            Path.Combine(baseDirectory, "..", "..", "..", "Files", "TestPPTX.pptx")
        );

        // when
        await using var fileStream = File.OpenRead(powerPintFilePath);
        var result = await _sut.GetTextAsync(fileStream, cancellationToken: AbortToken);

        // then
        result.Should().Contain("Second"); // Replace with actual expected content
    }

    [Fact]
    public async Task should_extract_text_from_a_non_seekable_stream()
    {
        // given: the shape of a blob download or a request body
        var path = Path.GetFullPath(
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "Files", "TestPPTX.pptx")
        );
        await using var stream = new NonSeekableStream(File.OpenRead(path));

        // when
        var result = await _sut.GetTextAsync(stream, cancellationToken: AbortToken);

        // then
        result.Should().Contain("Second");
    }

    [Fact]
    public async Task should_return_empty_string_when_get_text_async_presentation_has_no_slides()
    {
        await using var stream = _CreateEmptyPresentation();
        var result = await _sut.GetTextAsync(stream, cancellationToken: AbortToken);
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task should_return_text_from_multiple_slides_when_get_text_async()
    {
        var slideTexts = new[] { "Slide 1", "Slide 2", "Slide 3" };
        await using var stream = _CreatePresentationWithSlides(slideTexts);
        var result = await _sut.GetTextAsync(stream, cancellationToken: AbortToken);
        result.Should().Be(string.Join(Environment.NewLine, slideTexts) + Environment.NewLine);
    }

    [Fact]
    public async Task should_refuse_slide_text_past_the_extraction_limit()
    {
        // given: two slides, each under the per-part limit, whose text together passes 16 Mi characters
        var slideText = new string('A', 9 * 1024 * 1024);
        await using var stream = _CreatePresentationWithSlides([slideText, slideText]);

        // when
        var act = async () => await _sut.GetTextAsync(stream, cancellationToken: AbortToken);

        // then
        await act.Should().ThrowAsync<InvalidDataException>();
    }

    private static MemoryStream _CreateEmptyPresentation()
    {
        var stream = new MemoryStream();
        using var presentation = PresentationDocument.Create(stream, PresentationDocumentType.Presentation);
        presentation.AddPresentationPart();
        presentation.PresentationPart!.Presentation = new Presentation();
        presentation.Save();
        stream.Position = 0;

        return stream;
    }

    private static MemoryStream _CreatePresentationWithSlides(string[] texts)
    {
        var stream = new MemoryStream();
        using var presentation = PresentationDocument.Create(stream, PresentationDocumentType.Presentation);
        var presentationPart = presentation.AddPresentationPart();
        presentationPart.Presentation = new Presentation { SlideIdList = new SlideIdList() };

        uint slideId = 1;

        foreach (var text in texts)
        {
            var slidePart = presentationPart.AddNewPart<SlidePart>();

            var slide = new Slide(
                new CommonSlideData(new ShapeTree(new Shape(new TextBody(new Paragraph(new Run(new Text(text)))))))
            );

            slidePart.Slide = slide;

            presentationPart.Presentation.SlideIdList.Append(
                new SlideId { Id = slideId++, RelationshipId = presentationPart.GetIdOfPart(slidePart) }
            );
        }

        presentation.Save();
        stream.Position = 0;

        return stream;
    }
}
