using Framework.Imaging.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Framework.Imaging;

public static class AddImagingExtensions
{
    public static AddImagingBuilder AddImaging(
        this IServiceCollection services,
        Action<ImagingOptions>? configureOptions = null
    )
    {
        var optionsBuilder = services.AddOptions<ImagingOptions>();

        if (configureOptions is not null)
        {
            optionsBuilder.Configure(configureOptions);
        }

        services.AddSingleton<IImageResizer, ImageResizer>();
        services.AddSingleton<IImageCompressor, ImageCompressor>();

        return new(services);
    }
}
