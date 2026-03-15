using Headless.Jobs.Entities;
using Headless.Jobs.Interfaces;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Jobs.DependencyInjection;

public static class ServiceExtension
{
    public static TickerOptionsBuilder<TTimeTicker, TCronTicker> AddStackExchangeRedis<TTimeTicker, TCronTicker>(
        this TickerOptionsBuilder<TTimeTicker, TCronTicker> tickerConfiguration,
        Action<JobsRedisOptionBuilder> setupAction
    )
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        tickerConfiguration.ExternalProviderConfigServiceAction += services =>
        {
            var options = new JobsRedisOptionBuilder { InstanceName = "tickerq:" };

            setupAction?.Invoke(options);
            services.AddHostedService<NodeHeartBeatBackgroundService>();
            services.AddSingleton<IJobsRedisContext, JobsRedisContext>();
            services.AddKeyedSingleton<IDistributedCache>("tickerq", (sp, key) => new RedisCache(options));
            services.AddSingleton(_ => options);
        };

        return tickerConfiguration;
    }

    public class JobsRedisOptionBuilder : RedisCacheOptions
    {
        public TimeSpan NodeHeartbeatInterval { get; set; } = TimeSpan.FromMinutes(1);
    }
}
