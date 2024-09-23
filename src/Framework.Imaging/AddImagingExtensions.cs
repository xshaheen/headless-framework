// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Imaging.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Framework.Imaging;

public static class AddImagingExtensions
{
    public static AddImagingBuilder AddImaging(
        this IServiceCollection services,
        Action<ImagingOptions>? setupAction = null
    )
    {
        var optionsBuilder = services.AddOptions<ImagingOptions>();

        if (setupAction is not null)
        {
            optionsBuilder.Configure(setupAction);
        }

        services.AddSingleton<IImageResizer, ImageResizer>();
        services.AddSingleton<IImageCompressor, ImageCompressor>();

        return new(services);
    }

    public static AddImagingBuilder AddImaging(
        this IServiceCollection services,
        Action<ImagingOptions, IServiceProvider> setupAction
    )
    {
        services.AddOptions<ImagingOptions, ImagingOptionsValidator>().Configure(setupAction);
        services.AddSingleton<IImageResizer, ImageResizer>();
        services.AddSingleton<IImageCompressor, ImageCompressor>();

        return new(services);
    }
}
