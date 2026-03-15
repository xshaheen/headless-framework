using Headless.Jobs.Entities;
using Headless.Jobs.Interfaces;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Jobs.DependencyInjection;

public static class ServiceExtension
{
    public static JobsOptionsBuilder<TTimeTicker, TCronTicker> AddStackExchangeRedis<TTimeTicker, TCronTicker>(
        this JobsOptionsBuilder<TTimeTicker, TCronTicker> tickerConfiguration,
        Action<JobsRedisOptionBuilder> setupAction
    )
        where TTimeTicker : TimeJobEntity<TTimeTicker>, new()
        where TCronTicker : CronJobEntity, new()
    {
        tickerConfiguration.ExternalProviderConfigServiceAction += services =>
        {
            var options = new JobsRedisOptionBuilder { InstanceName = "jobs:" };

            setupAction?.Invoke(options);
            services.AddHostedService<NodeHeartBeatBackgroundService>();
            services.AddSingleton<IJobsRedisContext, JobsRedisContext>();
            services.AddKeyedSingleton<IDistributedCache>("jobs", (sp, key) => new RedisCache(options));
            services.AddSingleton(_ => options);
        };

        return tickerConfiguration;
    }

    public class JobsRedisOptionBuilder : RedisCacheOptions
    {
        public TimeSpan NodeHeartbeatInterval { get; set; } = TimeSpan.FromMinutes(1);
    }
}
