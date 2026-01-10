// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Framework.Imaging;

[PublicAPI]
public static class CoreSetup
{
    extension(IServiceCollection services)
    {
        public AddImagingBuilder AddImaging(IConfiguration config)
        {
            services.Configure<ImagingOptions, ImagingOptionsValidator>(config);

            return _AddCore(services);
        }

        public AddImagingBuilder AddImaging(Action<ImagingOptions>? setupAction = null)
        {
            services.Configure<ImagingOptions, ImagingOptionsValidator>(setupAction);

            return _AddCore(services);
        }

        public AddImagingBuilder AddImaging(Action<ImagingOptions, IServiceProvider> setupAction)
        {
            services.Configure<ImagingOptions, ImagingOptionsValidator>(setupAction);

            return _AddCore(services);
        }
    }

    private static AddImagingBuilder _AddCore(IServiceCollection services)
    {
        services.AddSingleton<IImageResizer, ImageResizer>();
        services.AddSingleton<IImageCompressor, ImageCompressor>();

        return new(services);
    }
}
