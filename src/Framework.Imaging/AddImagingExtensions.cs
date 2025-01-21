// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Imaging.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Framework.Imaging;

[PublicAPI]
public static class AddImagingExtensions
{
    public static AddImagingBuilder AddImaging(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<ImagingOptions, ImagingOptionsValidator>(config);

        return _AddCore(services);
    }

    public static AddImagingBuilder AddImaging(
        this IServiceCollection services,
        Action<ImagingOptions>? setupAction = null
    )
    {
        services.Configure<ImagingOptions, ImagingOptionsValidator>(setupAction);

        return _AddCore(services);
    }

    public static AddImagingBuilder AddImaging(
        this IServiceCollection services,
        Action<ImagingOptions, IServiceProvider> setupAction
    )
    {
        services.Configure<ImagingOptions, ImagingOptionsValidator>(setupAction);

        return _AddCore(services);
    }

    private static AddImagingBuilder _AddCore(IServiceCollection services)
    {
        services.AddSingleton<IImageResizer, ImageResizer>();
        services.AddSingleton<IImageCompressor, ImageCompressor>();

        return new(services);
    }
}
