// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Imaging.ImageSharp;

public static class ImageSharpSetup
{
    public static AddImagingBuilder AddImageSharpContributors(this AddImagingBuilder builder, IConfiguration config)
    {
        builder.Services.Configure<ImageSharpOptions, ImageSharpOptionsValidator>(config);

        return _AddCore(builder);
    }

    public static AddImagingBuilder AddImageSharpContributors(
        this AddImagingBuilder builder,
        Action<ImageSharpOptions>? setupAction = null
    )
    {
        builder.Services.Configure<ImageSharpOptions, ImageSharpOptionsValidator>(setupAction);

        return _AddCore(builder);
    }

    public static AddImagingBuilder AddImageSharpContributors(
        this AddImagingBuilder builder,
        Action<ImageSharpOptions, IServiceProvider> setupAction
    )
    {
        builder.Services.Configure<ImageSharpOptions, ImageSharpOptionsValidator>(setupAction);

        return _AddCore(builder);
    }

    private static AddImagingBuilder _AddCore(AddImagingBuilder builder)
    {
        builder.Services.AddSingleton<IImageResizerContributor, ImageSharpImageResizerContributor>();
        builder.Services.AddSingleton<IImageCompressorContributor, ImageSharpImageCompressorContributor>();

        return builder;
    }
}
