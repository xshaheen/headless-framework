using Framework.Constants;
using Framework.Imaging.Contracts;
using Framework.Imaging.ImageSharp;
using Microsoft.Extensions.Logging;

namespace Tests;

public class ImageSharpImageResizerContributorTests
{
    private readonly ImageSharpImageResizerContributor _imageResizerContributor;

    public ImageSharpImageResizerContributorTests()
    {
        var loggerMock = Substitute.For<ILogger<ImageSharpImageResizerContributor>>();
        _imageResizerContributor = new ImageSharpImageResizerContributor(loggerMock);
    }

    [Fact]
    public async Task should_resize_image_successfully()
    {
        // given
        var args = new ImageResizeArgs(50, 50, ContentTypes.Images.Jpeg);
        var cancellationToken = CancellationToken.None;

        await using var imageStream = new FileStream(_GetPathImage("happy-young-man-with-q-letter.jpg"), FileMode.Open, FileAccess.Read);

        // when
        var result = await _imageResizerContributor.TryResizeAsync(imageStream, args, cancellationToken);

        // then

        result.State.Should().Be(ImageProcessState.Done);
        result.Should().NotBeNull();
        result.Result?.MimeType.Should().Be(ContentTypes.Images.Jpeg);
    }

    [Fact]
    public async Task Should_Not_Resize_Unsupported_MimeType()
    {
        // given
        var args = new ImageResizeArgs(60, 60, "UnSupported");
        var cancellationToken = CancellationToken.None;

        await using var imageStream = new FileStream(_GetPathImage("Car1.jpg"), FileMode.Open, FileAccess.Read);

        // when
        var result = await _imageResizerContributor.TryResizeAsync(imageStream, args, cancellationToken);

        // then
        result.State.Should().Be(ImageProcessState.Unsupported);
        result.Error.Should().Be("The given image format is not supported.");
        result.Result.Should().BeNull();
    }


    [Fact]
    public async Task should_throw_exception_when_image_resize_mode_invalid()
    {
        // given
        var args = new ImageResizeArgs(50, 50, ContentTypes.Images.Jpeg,(ImageResizeMode) 12);
        var cancellationToken = CancellationToken.None;

        await using var imageStream = new FileStream(_GetPathImage("happy-young-man-with-q-letter.jpg"), FileMode.Open, FileAccess.Read);

        // when
        var act = async () => await _imageResizerContributor.TryResizeAsync(imageStream, args, cancellationToken);

        // then
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Unknown ImageResizeMode=12");
    }

    private static string _GetPathImage(string imageName)
    {
        var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
        return Path.Combine(baseDirectory, $@"..\..\..\Assets\{imageName}");
    }
}
