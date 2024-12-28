using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Drawing;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using Framework.Media.Indexing;
using Path = System.IO.Path;
using Shape = DocumentFormat.OpenXml.Presentation.Shape;
using Text = DocumentFormat.OpenXml.Drawing.Text;
using TextBody = DocumentFormat.OpenXml.Presentation.TextBody;

namespace Tests;

public sealed class PresentationDocumentMediaFileTextProviderTests
{
    private readonly PresentationDocumentMediaFileTextProvider _sut;

    public PresentationDocumentMediaFileTextProviderTests()
    {
        _sut = new PresentationDocumentMediaFileTextProvider();
    }

    [Fact]
    public async Task get_text_async_should_extract_text_from_power_point_file()
    {
        // given
        var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
        var powerPintFilePath = Path.Combine(baseDirectory, @"..\..\..\Files\TestPPTX.pptx");


        // when
        await using var fileStream = File.OpenRead(powerPintFilePath);
        var result = await _sut.GetTextAsync(powerPintFilePath, fileStream);

        // then
        result.Should().Contain("Second"); // Replace with actual expected content
    }

    [Fact]
    public async Task get_text_async_should_return_empty_string_when_presentation_has_no_slides()
    {
        await using var stream = _CreateEmptyPresentation();
        var result = await _sut.GetTextAsync("test.pptx", stream);
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task get_text_async_should_return_text_from_single_slide()
    {
        const string expectedText = "Test slide content";
        await using var stream = _CreatePresentationWithSlide(expectedText);
        var result = await _sut.GetTextAsync("test.pptx", stream);
        result.Should().Be($"{expectedText}\r\n");
    }

    [Fact]
    public async Task get_text_async_should_return_text_from_multiple_slides()
    {
        var slideTexts = new[] { "Slide 1", "Slide 2", "Slide 3" };
        await using var stream = _CreatePresentationWithSlides(slideTexts);
        var result = await _sut.GetTextAsync("test.pptx", stream);
        result.Should().Be(string.Join("\r\n", slideTexts) + "\r\n");
    }

    [Fact]
    public async Task should_throws_when_invalid_slide_references()
    {
        await using var stream = _CreatePresentationWithInvalidSlide();
        var result = await _sut.GetTextAsync("test.pptx", stream);
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task should_throws_when_null_parts_slide()
    {
        await using var stream = _CreatePresentationWithNoPartsSlide();
        var result = await _sut.GetTextAsync("test.pptx", stream);
        result.Should().BeEmpty();
    }

    private static Stream _CreateEmptyPresentation()
    {
        var stream = new MemoryStream();
        using var presentation = PresentationDocument.Create(stream, PresentationDocumentType.Presentation);
        presentation.AddPresentationPart();
        presentation.PresentationPart!.Presentation = new Presentation();
        presentation.Save();
        stream.Position = 0;

        return stream;
    }

    private static Stream _CreatePresentationWithSlide(string text)
    {
        var stream = new MemoryStream();
        using var presentation = PresentationDocument.Create(stream, PresentationDocumentType.Presentation);
        var presentationPart = presentation.AddPresentationPart();
        presentationPart.Presentation = new Presentation();

        var slidePart = presentation.PresentationPart!.AddNewPart<SlidePart>();

        var slide = new Slide(
            new CommonSlideData(
                new ShapeTree(
                    new Shape(
                        new TextBody(
                            new Paragraph(
                                new Run(new Text(text))
                            )
                        )
                    )
                )
            )
        );

        slidePart.Slide = slide;

        var slideIdList = new SlideIdList(new SlideId { Id = 1U, RelationshipId = presentationPart.GetIdOfPart(slidePart) });
        presentationPart.Presentation.SlideIdList = slideIdList;

        presentation.Save();
        stream.Position = 0;

        return stream;
    }

    private static Stream _CreatePresentationWithSlides(string[] texts)
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
                new CommonSlideData(
                    new ShapeTree(
                        new Shape(
                            new TextBody(
                                new Paragraph(
                                    new Run(new Text(text))
                                )
                            )
                        )
                    )
                )
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

    private static Stream _CreatePresentationWithInvalidSlide()
    {
        var stream = new MemoryStream();
        using var presentation = PresentationDocument.Create(stream, PresentationDocumentType.Presentation);
        var presentationPart = presentation.AddPresentationPart();

        presentationPart.Presentation = new Presentation
        {
            SlideIdList = new SlideIdList(new SlideId { Id = 1, RelationshipId = "invalid" }),
        };

        presentation.Save();
        stream.Position = 0;

        return stream;
    }

    private static Stream _CreatePresentationWithNoPartsSlide()
    {
        var stream = new MemoryStream();
        using var presentation = PresentationDocument.Create(stream, PresentationDocumentType.Presentation);
        var presentationPart = presentation.AddPresentationPart();
        var slidePart = presentation.PresentationPart!.AddNewPart<SlidePart>();

        presentationPart.Presentation = new Presentation
        {
            SlideIdList = new SlideIdList(new SlideId { Id = 1, RelationshipId = presentationPart.GetIdOfPart(slidePart) }),
        };

        presentation.Save();
        stream.Position = 0;

        return stream;
    }
}
