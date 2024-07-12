using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Framework.Blobs.FileSystem;

public static class AddFileSystemBlobExtensions
{
    public static IHostApplicationBuilder AddFileSystemBlobStorage(this IHostApplicationBuilder builder)
    {
        builder.Services.AddSingleton<IBlobNamingNormalizer, FileSystemBlobNamingNormalizer>();
        builder.Services.AddSingleton<IBlobStorage, FileSystemBlobStorage>();

        return builder;
    }
}
