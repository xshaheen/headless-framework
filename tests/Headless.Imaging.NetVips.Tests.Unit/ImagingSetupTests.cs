// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Http;
using Headless.Imaging;
using Headless.Testing.Tests;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class ImagingSetupTests : TestBase
{
    [Fact]
    public void should_register_pipeline_and_net_vips_contributors_when_use_net_vips()
    {
        // given
        var services = _CreateServices();

        // when
        services.AddHeadlessImaging(imaging => imaging.UseNetVips());
        using var provider = services.BuildServiceProvider();

        // then
        provider.GetRequiredService<IImageResizer>().Should().NotBeNull();
        provider.GetRequiredService<IImageCompressor>().Should().NotBeNull();
        provider
            .GetServices<IImageResizerContributor>()
            .Should()
            .ContainSingle()
            .Which.Should()
            .BeOfType<NetVipsImageResizerContributor>();
        provider
            .GetServices<IImageCompressorContributor>()
            .Should()
            .ContainSingle()
            .Which.Should()
            .BeOfType<NetVipsImageCompressorContributor>();
        provider.GetRequiredService<IImageInspector>().Should().NotBeNull();
        provider
            .GetServices<IImageInspectorContributor>()
            .Should()
            .ContainSingle()
            .Which.Should()
            .BeOfType<NetVipsImageInspectorContributor>();
    }

    [Fact]
    public void should_add_each_contributor_once_when_use_net_vips_is_called_twice()
    {
        // given
        var services = _CreateServices();

        // when
        services.AddHeadlessImaging(imaging => imaging.UseNetVips().UseNetVips());
        using var provider = services.BuildServiceProvider();

        // then
        provider.GetServices<IImageResizerContributor>().Should().ContainSingle();
        provider.GetServices<IImageCompressorContributor>().Should().ContainSingle();
        provider.GetServices<IImageInspectorContributor>().Should().ContainSingle();
    }

    [Fact]
    public void should_name_use_net_vips_when_no_provider_is_chosen()
    {
        // given
        var services = _CreateServices();

        // when
        var act = () => services.AddHeadlessImaging(_ => { });

        // then
        act.Should().Throw<InvalidOperationException>().WithMessage("*UseNetVips*");
    }

    [Fact]
    public void should_bind_net_vips_options_from_configuration()
    {
        // given
        var services = _CreateServices();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection([
                new KeyValuePair<string, string?>("JpegQuality", "82"),
                new KeyValuePair<string, string?>("StripMetadata", "false"),
                new KeyValuePair<string, string?>("MaxPixels", "1000000"),
                new KeyValuePair<string, string?>("CropFocus", "Attention"),
            ])
            .Build();

        // when
        services.AddHeadlessImaging(imaging => imaging.UseNetVips(configuration));
        using var provider = services.BuildServiceProvider();

        // then
        var options = provider.GetRequiredService<IOptions<NetVipsOptions>>().Value;
        options.JpegQuality.Should().Be(82);
        options.StripMetadata.Should().BeFalse();
        options.MaxPixels.Should().Be(1_000_000);
        options.CropFocus.Should().Be(NetVipsCropFocus.Attention);
    }

    [Fact]
    public void should_apply_net_vips_options_delegates()
    {
        // given
        var services = _CreateServices();

        // when
        services.AddHeadlessImaging(imaging =>
            imaging.UseNetVips(options => options.WebpQuality = 60).UseNetVips((options, _) => options.AvifQuality = 40)
        );
        using var provider = services.BuildServiceProvider();

        // then
        var options = provider.GetRequiredService<IOptions<NetVipsOptions>>().Value;
        options.WebpQuality.Should().Be(60);
        options.AvifQuality.Should().Be(40);
    }

    [Fact]
    public void should_default_to_secure_options()
    {
        // given
        var options = new NetVipsOptions();

        // then
        options.StripMetadata.Should().BeTrue();
        options.MaxPixels.Should().Be(16_383L * 16_383L);
        options.CropFocus.Should().Be(NetVipsCropFocus.Center);
    }

    [Theory]
    [InlineData("JpegQuality", "0")]
    [InlineData("WebpQuality", "101")]
    [InlineData("AvifQuality", "0")]
    [InlineData("PngCompressionLevel", "10")]
    [InlineData("MaxPixels", "0")]
    [InlineData("CropFocus", "99")]
    public void should_reject_invalid_net_vips_options(string key, string value)
    {
        // given
        var services = _CreateServices();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection([new KeyValuePair<string, string?>(key, value)])
            .Build();
        services.AddHeadlessImaging(imaging => imaging.UseNetVips(configuration));
        using var provider = services.BuildServiceProvider();

        // when
        var act = () => provider.GetRequiredService<IOptions<NetVipsOptions>>().Value;

        // then
        act.Should().Throw<OptionsValidationException>();
    }

    [Fact]
    public async Task should_resize_through_the_pipeline_with_the_default_mode_and_a_non_seekable_stream()
    {
        // given
        var services = _CreateServices();
        services.AddHeadlessImaging(imaging =>
            imaging.Configure(options => options.DefaultResizeMode = ImageResizeMode.Max).UseNetVips()
        );
        await using var provider = services.BuildServiceProvider();
        var resizer = provider.GetRequiredService<IImageResizer>();
        await using var input = new NonSeekableStream(TestImages.Encode(".webp", 200, 100));
        var args = new ImageResizeArgs(ImageResizeMode.Max, 50, 50) { Mode = ImageResizeMode.Default };

        // when
        var result = await resizer.ResizeAsync(input, args, AbortToken);

        // then
        result.State.Should().Be(ImageProcessState.Done);
        result.Result!.MimeType.Should().Be(ContentTypes.Images.Webp);
        result.Result.Width.Should().Be(50);
        result.Result.Height.Should().Be(25);
    }

    [Fact]
    public async Task should_inspect_through_the_pipeline_and_fall_through_an_unsupported_contributor()
    {
        // given: a custom contributor registered after NetVips is tried first and declines
        var services = _CreateServices();
        services.AddHeadlessImaging(imaging => imaging.UseNetVips());
        var declining = Substitute.For<IImageInspectorContributor>();
        declining
            .TryInspectAsync(Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .Returns(ImageInspectResult.NotSupported());
        services.AddSingleton(declining);
        await using var provider = services.BuildServiceProvider();
        await using var input = new NonSeekableStream(TestImages.Encode(".png", 30, 20));

        // when
        var result = await provider.GetRequiredService<IImageInspector>().InspectAsync(input, AbortToken);

        // then
        await declining.Received(1).TryInspectAsync(Arg.Any<Stream>(), Arg.Any<CancellationToken>());
        result.Result!.MimeType.Should().Be(ContentTypes.Images.Png);
        result.Result.Width.Should().Be(30);
    }

    [Fact]
    public async Task should_report_unsupported_when_no_contributor_handles_the_image()
    {
        // given
        var services = _CreateServices();
        services.AddHeadlessImaging(imaging => imaging.UseNetVips());
        await using var provider = services.BuildServiceProvider();
        await using var input = new MemoryStream(TestImages.Svg);

        // when
        var result = await provider.GetRequiredService<IImageInspector>().InspectAsync(input, AbortToken);

        // then
        result.State.Should().Be(ImageProcessState.Unsupported);
    }

    [Fact]
    public async Task should_report_an_unreadable_stream()
    {
        // given
        var services = _CreateServices();
        services.AddHeadlessImaging(imaging => imaging.UseNetVips());
        await using var provider = services.BuildServiceProvider();
        var input = new MemoryStream([1, 2, 3]);
        await input.DisposeAsync();

        // when
        var result = await provider.GetRequiredService<IImageInspector>().InspectAsync(input, AbortToken);

        // then
        result.State.Should().Be(ImageProcessState.Unsupported);
        result.Error.Should().Be("Cannot read the image.");
    }

    private static ServiceCollection _CreateServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        return services;
    }

    private sealed class NonSeekableStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
    }
}
