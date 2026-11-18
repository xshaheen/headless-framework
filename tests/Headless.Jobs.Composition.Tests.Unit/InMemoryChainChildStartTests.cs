// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests;

/// <summary>Runs the cross-store chain child start scenario on the in-memory store, through a real host.</summary>
public sealed class InMemoryChainChildStartTests : TestBase
{
    [Fact]
    public async Task should_record_a_running_chain_childs_start()
    {
        using var host = new HostBuilder()
            .ConfigureServices(services =>
                services.AddHeadlessJobs(jobs => jobs.AddModule<ChainChildStartJobsModule>())
            )
            .Build();
        await host.StartAsync(AbortToken);

        try
        {
            var store = host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
            await JobsChainChildStartScenarios.RunningChildReadsInProgressAsync(store, AbortToken);
        }
        finally
        {
            await host.StopAsync(CancellationToken.None);
        }
    }
}
