using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Framework.Blobs.Azure;

public static class Extensions
{
    public static IHostApplicationBuilder AddAzureBlobStorage(
        this IHostApplicationBuilder builder,
        string configSectionName = "Azure:Blobs"
    )
    {
        var config = builder.Configuration.GetSection(configSectionName);

        builder.Services.ConfigureSingleton<AzureStorageOptions, AzureStorageOptionsValidator>(config);
        builder.Services.AddSingleton<IBlobNamingNormalizer, AzureBlobNamingNormalizer>();
        builder.Services.AddSingleton<IBlobStorage, AzureBlobStorage>();

        // builder.Services.AddAzureClients(factoryBuilder =>
        // {
        //     factoryBuilder.AddBlobServiceClient());
        //     builder.UseCredential(new EnvironmentCredential());
        // });

        return builder;
    }
}
