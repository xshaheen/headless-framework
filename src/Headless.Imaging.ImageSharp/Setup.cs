// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Imaging.ImageSharp;

/// <summary>
/// Registers the ImageSharp-based resize and compress contributors with the imaging pipeline.
/// </summary>
public static class SetupImageSharp
{
    /// <summary>
    /// Adds the ImageSharp contributors and binds <see cref="ImageSharpOptions"/> from
    /// <paramref name="config"/>.
    /// </summary>
    /// <param name="builder">The imaging builder returned by <c>AddImaging</c>.</param>
    /// <param name="config">
    /// Configuration section whose keys map to <see cref="ImageSharpOptions"/> properties.
    /// </param>
    /// <returns>The same <paramref name="builder"/> for further chaining.</returns>
    public static AddImagingBuilder AddImageSharpContributors(this AddImagingBuilder builder, IConfiguration config)
    {
        builder.Services.Configure<ImageSharpOptions, ImageSharpOptionsValidator>(config);

        return _AddImageSharpCore(builder);
    }

    /// <summary>
    /// Adds the ImageSharp contributors and optionally configures <see cref="ImageSharpOptions"/>
    /// via <paramref name="setupAction"/>.
    /// </summary>
    /// <param name="builder">The imaging builder returned by <c>AddImaging</c>.</param>
    /// <param name="setupAction">
    /// Optional delegate to configure <see cref="ImageSharpOptions"/>. When <see langword="null"/>,
    /// default option values are used.
    /// </param>
    /// <returns>The same <paramref name="builder"/> for further chaining.</returns>
    public static AddImagingBuilder AddImageSharpContributors(
        this AddImagingBuilder builder,
        Action<ImageSharpOptions>? setupAction = null
    )
    {
        builder.Services.Configure<ImageSharpOptions, ImageSharpOptionsValidator>(setupAction);

        return _AddImageSharpCore(builder);
    }

    /// <summary>
    /// Adds the ImageSharp contributors and configures <see cref="ImageSharpOptions"/> via a
    /// factory delegate that receives the <see cref="IServiceProvider"/>.
    /// </summary>
    /// <param name="builder">The imaging builder returned by <c>AddImaging</c>.</param>
    /// <param name="setupAction">
    /// Delegate that configures <see cref="ImageSharpOptions"/> using the resolved service provider.
    /// </param>
    /// <returns>The same <paramref name="builder"/> for further chaining.</returns>
    public static AddImagingBuilder AddImageSharpContributors(
        this AddImagingBuilder builder,
        Action<ImageSharpOptions, IServiceProvider> setupAction
    )
    {
        builder.Services.Configure<ImageSharpOptions, ImageSharpOptionsValidator>(setupAction);

        return _AddImageSharpCore(builder);
    }

    private static AddImagingBuilder _AddImageSharpCore(AddImagingBuilder builder)
    {
        builder.Services.AddSingleton<IImageResizerContributor, ImageSharpImageResizerContributor>();
        builder.Services.AddSingleton<IImageCompressorContributor, ImageSharpImageCompressorContributor>();

        return builder;
    }
}
