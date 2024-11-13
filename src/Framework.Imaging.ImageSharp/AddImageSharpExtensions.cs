// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Imaging.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Framework.Imaging.ImageSharp;

public static class AddImageSharpExtensions
{
    public static AddImagingBuilder AddImageSharpContributors(this AddImagingBuilder builder, IConfiguration config)
    {
        builder.Services.ConfigureSingleton<ImageSharpOptions, ImageSharpOptionsValidator>(config);

        return _AddCore(builder);
    }

    public static AddImagingBuilder AddImageSharpContributors(
        this AddImagingBuilder builder,
        Action<ImageSharpOptions>? setupAction = null
    )
    {
        builder.Services.ConfigureSingleton<ImageSharpOptions, ImageSharpOptionsValidator>(setupAction);

        return _AddCore(builder);
    }

    public static AddImagingBuilder AddImageSharpContributors(
        this AddImagingBuilder builder,
        Action<ImageSharpOptions, IServiceProvider> setupAction
    )
    {
        builder.Services.ConfigureSingleton<ImageSharpOptions, ImageSharpOptionsValidator>(setupAction);

        return _AddCore(builder);
    }

    private static AddImagingBuilder _AddCore(AddImagingBuilder builder)
    {
        builder.Services.AddSingleton<IImageResizerContributor, ImageSharpImageResizerContributor>();
        builder.Services.AddSingleton<IImageCompressorContributor, ImageSharpImageCompressorContributor>();

        return builder;
    }
}
