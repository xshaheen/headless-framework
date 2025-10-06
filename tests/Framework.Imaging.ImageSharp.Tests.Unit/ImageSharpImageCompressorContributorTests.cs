using Framework.Constants;
using Framework.Imaging;
using Framework.Imaging.ImageSharp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class ImageSharpImageCompressorContributorTests
{
    private readonly ImageSharpImageCompressorContributor _compressorContributor;

    public ImageSharpImageCompressorContributorTests()
    {
        var options = new ImageSharpOptions();
        var optionsMock = Substitute.For<IOptions<ImageSharpOptions>>();
        optionsMock.Value.Returns(options);

        var loggerMock = Substitute.For<ILogger<ImageSharpImageCompressorContributor>>();

        _compressorContributor = new ImageSharpImageCompressorContributor(optionsMock, loggerMock);
    }

    [Fact]
    public async Task should_compress_image_when_original_image_greater_than_compressed_image()
    {
        // given
        var args = new ImageCompressArgs(ContentTypes.Images.Webp);
        var cancellationToken = CancellationToken.None;

        // when
        await using var imageStream = new FileStream(
            _GetPathImage("happy-young-man-with-q-letter.jpg"),
            FileMode.Open,
            FileAccess.Read
        );

        var result = await _compressorContributor.TryCompressAsync(imageStream, args, cancellationToken);

        // then
        result.Result.Should().NotBeNull();
        result.Result.Length.Should().BeLessThan(imageStream.Length);
        result.State.Should().Be(ImageProcessState.Done);
        result.Should().BeOfType<ImageStreamCompressResult>();
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task should_not_compress_image_when_original_image_less_than_compressed_image()
    {
        // given
        var args = new ImageCompressArgs(ContentTypes.Images.Webp);
        var cancellationToken = CancellationToken.None;

        // when
        await using var imageStream = new FileStream(_GetPathImage("Car1.jpg"), FileMode.Open, FileAccess.Read);

        var result = await _compressorContributor.TryCompressAsync(imageStream, args, cancellationToken);

        // then
        result.Result.Should().BeNull();
        result.Error.Should().Be("The compressed image is larger than the original.");
        result.State.Should().Be(ImageProcessState.Failed);
    }

    private static string _GetPathImage(string imageName)
    {
        var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
        return Path.Combine(baseDirectory, $@"..\..\..\Assets\{imageName}");
    }
}
