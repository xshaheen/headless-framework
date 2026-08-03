// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Constants;
using Headless.Imaging;
using Headless.Imaging.ImageSharp;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Diagnostics;
using SixLabors.ImageSharp.PixelFormats;

namespace Tests;

/// <summary>
/// Pins that both contributors release the decoded <see cref="Image"/> on every exit path.
/// <see cref="MemoryDiagnostics.TotalUndisposedAllocationCount"/> counts ImageSharp memory groups that are alive and
/// not yet released, so a decode that is never disposed leaves the count above the baseline until a finalizer
/// happens to run. The counter is process-global, hence <see cref="ImageSharpTestCollection"/>.
/// </summary>
[Collection(ImageSharpTestCollection.Name)]
public sealed class ImageSharpContributorDisposalTests : TestBase
{
    private readonly ImageSharpImageCompressorContributor _compressor;
    private readonly ImageSharpImageResizerContributor _resizer;

    public ImageSharpContributorDisposalTests()
    {
        var optionsAccessor = Substitute.For<IOptions<ImageSharpOptions>>();
        optionsAccessor.Value.Returns(new ImageSharpOptions());

        _compressor = new ImageSharpImageCompressorContributor(
            optionsAccessor,
            NullLogger<ImageSharpImageCompressorContributor>.Instance
        );

        _resizer = new ImageSharpImageResizerContributor(NullLogger<ImageSharpImageResizerContributor>.Instance);
    }

    #region Compressor

    [Fact]
    public async Task compress_releases_the_decoded_image_when_the_output_is_smaller()
    {
        await using var imageStream = _OpenAsset("happy-young-man-with-q-letter.jpg");
        var baseline = MemoryDiagnostics.TotalUndisposedAllocationCount;

        var result = await _compressor.TryCompressAsync(
            imageStream,
            new ImageCompressArgs(ContentTypes.Images.Webp),
            AbortToken
        );

        result.State.Should().Be(ImageProcessState.Done);
        MemoryDiagnostics.TotalUndisposedAllocationCount.Should().Be(baseline);

        // The returned stream is independent of the disposed image and must still be readable.
        await using var content = result.Result!;
        content.Length.Should().BePositive();
    }

    [Fact]
    public async Task compress_releases_the_decoded_image_when_the_output_is_larger()
    {
        await using var imageStream = _OpenAsset("Car1.jpg");
        var baseline = MemoryDiagnostics.TotalUndisposedAllocationCount;

        var result = await _compressor.TryCompressAsync(
            imageStream,
            new ImageCompressArgs(ContentTypes.Images.Webp),
            AbortToken
        );

        result.State.Should().Be(ImageProcessState.Failed);
        MemoryDiagnostics.TotalUndisposedAllocationCount.Should().Be(baseline);
    }

    [Fact]
    public async Task compress_releases_the_decoded_image_when_the_decoded_format_is_not_compressible()
    {
        // BMP decodes fine but is not a compression target, so the contributor returns early while holding a live
        // decoded image — the exit path the leak used to escape through.
        await using var imageStream = await _CreateImageStreamAsync(
            static (image, stream) => image.SaveAsBmpAsync(stream)
        );
        var baseline = MemoryDiagnostics.TotalUndisposedAllocationCount;

        var result = await _compressor.TryCompressAsync(imageStream, new ImageCompressArgs(), AbortToken);

        result.State.Should().Be(ImageProcessState.Unsupported);
        result.Error.Should().Contain(ContentTypes.Images.Bmp);
        MemoryDiagnostics.TotalUndisposedAllocationCount.Should().Be(baseline);
    }

    #endregion

    #region Resizer

    [Fact]
    public async Task resize_releases_the_decoded_image_when_the_image_is_resized()
    {
        await using var imageStream = _OpenAsset("happy-young-man-with-q-letter.jpg");
        var baseline = MemoryDiagnostics.TotalUndisposedAllocationCount;

        var result = await _resizer.TryResizeAsync(
            imageStream,
            new ImageResizeArgs(ImageResizeMode.Min, 344, 300, ContentTypes.Images.Jpeg),
            AbortToken
        );

        result.State.Should().Be(ImageProcessState.Done);
        MemoryDiagnostics.TotalUndisposedAllocationCount.Should().Be(baseline);

        await using var content = result.Result!.Content;
        content.Length.Should().BePositive();
    }

    [Fact]
    public async Task resize_releases_the_decoded_image_without_disposing_the_caller_stream_on_pass_through()
    {
        var original = await File.ReadAllBytesAsync(_GetAssetPath("happy-young-man-with-q-letter.jpg"), AbortToken);
        await using var imageStream = new MemoryStream(original.Length);
        await imageStream.WriteAsync(original, AbortToken);
        imageStream.Position = 0;

        var baseline = MemoryDiagnostics.TotalUndisposedAllocationCount;

        // ImageResizeMode.None short-circuits to a pass-through that hands the caller's own stream back, so
        // releasing the decoded image must not touch that stream.
        var result = await _resizer.TryResizeAsync(
            imageStream,
            new ImageResizeArgs(ImageResizeMode.None, 10, 10),
            AbortToken
        );

        result.State.Should().Be(ImageProcessState.Done);
        MemoryDiagnostics.TotalUndisposedAllocationCount.Should().Be(baseline);

        result.Result!.Content.Should().BeSameAs(imageStream);
        imageStream.Position = 0;
        var roundTripped = new byte[original.Length];
        await imageStream.ReadExactlyAsync(roundTripped, AbortToken);
        roundTripped.Should().Equal(original);
    }

    [Fact]
    public async Task resize_releases_the_decoded_image_when_the_decoded_format_is_not_resizable()
    {
        // TGA decodes fine but is outside the resizer's supported set, so the contributor returns early while
        // holding a live decoded image.
        await using var imageStream = await _CreateImageStreamAsync(
            static (image, stream) => image.SaveAsTgaAsync(stream)
        );
        var baseline = MemoryDiagnostics.TotalUndisposedAllocationCount;

        var result = await _resizer.TryResizeAsync(
            imageStream,
            new ImageResizeArgs(ImageResizeMode.Min, 32, 32),
            AbortToken
        );

        result.State.Should().Be(ImageProcessState.Unsupported);
        MemoryDiagnostics.TotalUndisposedAllocationCount.Should().Be(baseline);
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Builds a small in-memory image in the given format. The dimensions are deliberately above ImageSharp's
    /// shared-array-pool threshold so the decode registers with <see cref="MemoryDiagnostics"/>.
    /// </summary>
    private static async Task<Stream> _CreateImageStreamAsync(Func<Image<Rgba32>, Stream, Task> saveAsync)
    {
        using var image = new Image<Rgba32>(256, 256, Color.CornflowerBlue.ToPixel<Rgba32>());
        var stream = new MemoryStream();

        try
        {
            await saveAsync(image, stream);
            stream.Position = 0;

            return stream;
        }
        catch
        {
            await stream.DisposeAsync();

            throw;
        }
    }

    private static FileStream _OpenAsset(string imageName)
    {
        return new FileStream(_GetAssetPath(imageName), FileMode.Open, FileAccess.Read);
    }

    private static string _GetAssetPath(string imageName)
    {
        var separator = Path.DirectorySeparatorChar;
        var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;

        return Path.Combine(baseDirectory, $"..{separator}..{separator}..{separator}Assets{separator}{imageName}");
    }

    #endregion
}

/// <summary>
/// Serializes every test that decodes with ImageSharp: the disposal assertions read the process-global
/// <see cref="MemoryDiagnostics.TotalUndisposedAllocationCount"/>, which a concurrently decoding test would perturb.
/// </summary>
[CollectionDefinition(Name)]
public sealed class ImageSharpTestCollection
{
    public const string Name = "imagesharp-memory-diagnostics";
}
