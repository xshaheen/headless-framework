// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Imaging.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Framework.Imaging.ImageSharp;

public static class AddImageSharpExtensions
{
    public static AddImagingBuilder AddImageSharpContributors(
        this AddImagingBuilder builder,
        Action<ImageSharpOptions>? configureOptions = null
    )
    {
        var optionsBuilder = builder.Services.AddOptions<ImageSharpOptions>();

        if (configureOptions is not null)
        {
            optionsBuilder.Configure(configureOptions);
        }

        builder.Services.AddSingleton<IImageResizerContributor, ImageSharpImageResizerContributor>();
        builder.Services.AddSingleton<IImageCompressorContributor, ImageSharpImageCompressorContributor>();

        return builder;
    }
}
