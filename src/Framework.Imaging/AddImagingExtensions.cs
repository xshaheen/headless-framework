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
        Action<ImagingOptions, IServiceProvider>? setupAction = null
    )
    {
        var optionsBuilder = services.AddOptions<ImagingOptions, ImagingOptionsValidator>();

        if (setupAction is not null)
        {
            optionsBuilder.Configure(setupAction);
        }

        services.AddSingleton<IImageResizer, ImageResizer>();
        services.AddSingleton<IImageCompressor, ImageCompressor>();

        return new(services);
    }
}
