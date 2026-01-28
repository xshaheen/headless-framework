using Headless.Ticker.Entities;
using Headless.Ticker.Interfaces;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Ticker.DependencyInjection;

public static class ServiceExtension
{
    public static TickerOptionsBuilder<TTimeTicker, TCronTicker> AddStackExchangeRedis<TTimeTicker, TCronTicker>(
        this TickerOptionsBuilder<TTimeTicker, TCronTicker> tickerConfiguration,
        Action<TickerQRedisOptionBuilder> setupAction
    )
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        tickerConfiguration.ExternalProviderConfigServiceAction += services =>
        {
            var options = new TickerQRedisOptionBuilder { InstanceName = "tickerq:" };

            setupAction?.Invoke(options);
            services.AddHostedService<NodeHeartBeatBackgroundService>();
            services.AddSingleton<ITickerQRedisContext, TickerQRedisContext>();
            services.AddKeyedSingleton<IDistributedCache>("tickerq", (sp, key) => new RedisCache(options));
            services.AddSingleton(_ => options);
        };

        return tickerConfiguration;
    }

    public class TickerQRedisOptionBuilder : RedisCacheOptions
    {
        public TimeSpan NodeHeartbeatInterval { get; set; } = TimeSpan.FromMinutes(1);
    }
}
