using Headless.Jobs.Entities;
using Headless.Jobs.Interfaces;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Jobs;

public static class ServiceExtension
{
    public static JobsOptionsBuilder<TTimeJob, TCronJob> AddStackExchangeRedis<TTimeJob, TCronJob>(
        this JobsOptionsBuilder<TTimeJob, TCronJob> jobsConfiguration,
        Action<JobsRedisOptionBuilder>? setupAction = null
    )
        where TTimeJob : TimeJobEntity<TTimeJob>, new()
        where TCronJob : CronJobEntity, new()
    {
        jobsConfiguration.ExternalProviderConfigServiceAction += services =>
        {
            var options = new JobsRedisOptionBuilder { InstanceName = "jobs:" };

            setupAction?.Invoke(options);
            services.AddHostedService<NodeHeartBeatBackgroundService>();
            services.AddSingleton<IJobsRedisContext, JobsRedisContext>();
            services.AddKeyedSingleton<IDistributedCache>("jobs", (sp, key) => new RedisCache(options));
            services.AddSingleton(_ => options);
        };

        return jobsConfiguration;
    }

    public sealed class JobsRedisOptionBuilder : RedisCacheOptions
    {
        public TimeSpan NodeHeartbeatInterval { get; set; } = TimeSpan.FromMinutes(1);
    }
}
