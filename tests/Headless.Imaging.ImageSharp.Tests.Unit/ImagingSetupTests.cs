// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Imaging;
using Headless.Imaging.ImageSharp;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class ImagingSetupTests
{
    [Fact]
    public void should_register_pipeline_and_image_sharp_contributors_when_use_image_sharp()
    {
        // given
        var services = _CreateServices();

        // when
        services.AddHeadlessImaging(imaging => imaging.UseImageSharp());
        using var provider = services.BuildServiceProvider();

        // then
        provider.GetRequiredService<IImageResizer>().Should().NotBeNull();
        provider.GetRequiredService<IImageCompressor>().Should().NotBeNull();
        provider
            .GetServices<IImageResizerContributor>()
            .Should()
            .ContainSingle()
            .Which.Should()
            .BeOfType<ImageSharpImageResizerContributor>();
        provider
            .GetServices<IImageCompressorContributor>()
            .Should()
            .ContainSingle()
            .Which.Should()
            .BeOfType<ImageSharpImageCompressorContributor>();
    }

    [Fact]
    public void should_add_each_contributor_once_when_use_image_sharp_is_called_twice()
    {
        // given
        var services = _CreateServices();

        // when
        services.AddHeadlessImaging(imaging => imaging.UseImageSharp().UseImageSharp());
        using var provider = services.BuildServiceProvider();

        // then
        provider.GetServices<IImageResizerContributor>().Should().ContainSingle();
        provider.GetServices<IImageCompressorContributor>().Should().ContainSingle();
    }

    [Fact]
    public void should_throw_when_no_provider_is_chosen()
    {
        // given
        var services = _CreateServices();

        // when
        var act = () => services.AddHeadlessImaging(_ => { });

        // then
        act.Should().Throw<InvalidOperationException>().WithMessage("*UseImageSharp*");
    }

    [Fact]
    public void should_throw_when_imaging_is_registered_twice()
    {
        // given
        var services = _CreateServices();
        services.AddHeadlessImaging(imaging => imaging.UseImageSharp());

        // when
        var act = () => services.AddHeadlessImaging(imaging => imaging.UseImageSharp());

        // then
        act.Should().Throw<InvalidOperationException>().WithMessage("*already called*");
    }

    [Fact]
    public void should_bind_imaging_options_from_configuration()
    {
        // given
        var services = _CreateServices();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection([new KeyValuePair<string, string?>("DefaultResizeMode", "Crop")])
            .Build();

        // when
        services.AddHeadlessImaging(imaging => imaging.Configure(configuration).UseImageSharp());
        using var provider = services.BuildServiceProvider();

        // then
        provider
            .GetRequiredService<IOptions<ImagingOptions>>()
            .Value.DefaultResizeMode.Should()
            .Be(ImageResizeMode.Crop);
    }

    [Fact]
    public void should_apply_image_sharp_options_delegate()
    {
        // given
        var services = _CreateServices();

        // when
        services.AddHeadlessImaging(imaging =>
            imaging.UseImageSharp((options, _) => options.DefaultCompressQuality = 90)
        );
        using var provider = services.BuildServiceProvider();

        // then
        provider.GetRequiredService<IOptions<ImageSharpOptions>>().Value.DefaultCompressQuality.Should().Be(90);
    }

    [Fact]
    public void should_reject_invalid_image_sharp_options()
    {
        // given
        var services = _CreateServices();
        services.AddHeadlessImaging(imaging => imaging.UseImageSharp(options => options.DefaultCompressQuality = 0));
        using var provider = services.BuildServiceProvider();

        // when
        var act = () => provider.GetRequiredService<IOptions<ImageSharpOptions>>().Value;

        // then
        act.Should().Throw<OptionsValidationException>();
    }

    private static ServiceCollection _CreateServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        return services;
    }
}
