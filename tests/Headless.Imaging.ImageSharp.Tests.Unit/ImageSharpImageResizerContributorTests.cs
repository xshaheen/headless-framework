using Headless.Constants;
using Headless.Imaging;
using Headless.Imaging.ImageSharp;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging;

namespace Tests;

public sealed class ImageSharpImageResizerContributorTests : TestBase
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
        const int width = 344;
        const int height = 300;
        var args = new ImageResizeArgs(ImageResizeMode.Min, width, height, ContentTypes.Images.Jpeg);

        await using var imageStream = new FileStream(
            _GetPathImage("happy-young-man-with-q-letter.jpg"),
            FileMode.Open,
            FileAccess.Read
        );

        // when
        var result = await _imageResizerContributor.TryResizeAsync(imageStream, args, AbortToken);

        // then

        result.State.Should().Be(ImageProcessState.Done);
        result.Should().NotBeNull();
        result.Result!.MimeType.Should().Be(ContentTypes.Images.Jpeg);
        result.Result.Width.Should().Be(width);
        result.Result.Height.Should().Be(height);
        result.Result.Content.Should().NotBeNull();
        result.Result.Content.Length.Should().BePositive();
    }

    // bugs not change mimeType
    [Fact]
    public async Task should_change_mime_type_successfully()
    {
        // given
        const string mimeType = ContentTypes.Images.Png;
        var args = new ImageResizeArgs(mimeType);

        await using var imageStream = new FileStream(
            _GetPathImage("happy-young-man-with-q-letter.jpg"),
            FileMode.Open,
            FileAccess.Read
        );

        // when
        var result = await _imageResizerContributor.TryResizeAsync(imageStream, args, AbortToken);

        // then
        result.State.Should().Be(ImageProcessState.Done);
        result.Result!.MimeType.Should().Be(mimeType);
    }

    [Fact]
    public async Task should_resize_successfully_with_constructor_take_mode_and_height_required()
    {
        // given
        const int height = 200;
        var args = new ImageResizeArgs(ImageResizeMode.Crop, null, height);

        await using var imageStream = new FileStream(
            _GetPathImage("happy-young-man-with-q-letter.jpg"),
            FileMode.Open,
            FileAccess.Read
        );

        // when
        var result = await _imageResizerContributor.TryResizeAsync(imageStream, args, AbortToken);

        // then
        result.State.Should().Be(ImageProcessState.Done);
        result.Should().NotBeNull();
        result.Result!.MimeType.Should().Be(ContentTypes.Images.Jpeg);
        result.Result.Height.Should().Be(height);
        result.Result.Content.Should().NotBeNull();
        result.Result.Content.Length.Should().BePositive();
    }

    private static string _GetPathImage(string imageName)
    {
        var separator = Path.DirectorySeparatorChar;
        var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;

        return Path.Combine(baseDirectory, $"..{separator}..{separator}..{separator}Assets{separator}{imageName}");
    }
}
