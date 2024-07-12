using EasyCaching.Core.Configurations;
using EasyCaching.Serialization.MemoryPack;
using EasyCaching.Serialization.SystemTextJson.Configurations;
using Framework.BuildingBlocks.Constants;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Framework.Caching.EasyCache;

public static class AddEasyCacheExtensions
{
    private const string _JsonSerializerName = "json";
    private const string _MemSerializerName = "memory";

    public static IHostApplicationBuilder AddInMemoryEasyCache(this IHostApplicationBuilder builder)
    {
        builder.Services.AddEasyCaching(options =>
        {
            options.WithMemoryPack(_MemSerializerName);

            options.UseInMemory(config =>
            {
                config.CacheNulls = false;
                config.SerializerName = _MemSerializerName;
            });
        });

        builder.Services.AddSingleton<ICache, EasyCachingCache>();

        return builder;
    }

    public static IHostApplicationBuilder AddRedisCache(
        this IHostApplicationBuilder builder,
        string connectionString,
        string? keyPrefix = null
    )
    {
        builder.Services.AddSingleton<ICache, EasyCachingCache>();

        builder.Services.AddEasyCaching(options =>
        {
            options.WithSystemTextJson(o => PlatformJsonConstants.ConfigureInternalJsonOptions(o), _JsonSerializerName);

            options.UseRedis(config =>
            {
                var parts = connectionString.Split(':');
                var host = parts[0];
                var port = int.Parse(parts[1], CultureInfo.InvariantCulture);
                config.DBConfig.Endpoints.Add(new ServerEndPoint(host, port));
                config.DBConfig.Configuration = connectionString;
                config.DBConfig.KeyPrefix = keyPrefix;
                config.DBConfig.AllowAdmin = true;
                config.DBConfig.Database = 0;
                config.SerializerName = _JsonSerializerName;
            });
        });

        return builder;
    }
}
