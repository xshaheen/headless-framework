// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;
using Headless.Jobs;
using Headless.Jobs.Provider;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Tests.Provider;

/// <summary>Runs the cross-store chain reclaim scenarios on the in-memory store, on a fake clock.</summary>
public sealed class InMemoryChainReclaimTests : TestBase
{
    [Fact]
    public Task should_run_a_chain_child_on_another_node_when_its_node_dies_after_the_root_succeeded()
    {
        return JobsChainReclaimScenarios.DeadNodeChildRunsOnAnotherNodeAsync(new _Backend(), AbortToken);
    }

    [Fact]
    public Task should_run_a_chain_child_again_when_its_lease_lapses_after_the_root_succeeded()
    {
        return JobsChainReclaimScenarios.StalledChildRunsOnAnotherNodeAsync(new _Backend(), AbortToken);
    }

    // One process is the only node an in-memory store has, so it plays both nodes; advancing its clock lapses every lease.
    private sealed class _Backend : IChainReclaimScenarioBackend
    {
        private static readonly TimeSpan _LeaseDuration = TimeSpan.FromMinutes(5);
        private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 06, 17, 12, 00, 00, TimeSpan.Zero));

        public _Backend()
        {
            var services = new ServiceCollection();
            services.AddSingleton<TimeProvider>(_clock);
            services.AddHeadlessGuidGenerator();
            services.AddSingleton(new SchedulerOptionsBuilder { NodeId = "node-a", LeaseDuration = _LeaseDuration });
            DeadNode = new JobsInMemoryPersistenceProvider<TimeJobEntity, CronJobEntity>(
                services.BuildServiceProvider()
            );
        }

        public IJobPersistenceProvider<TimeJobEntity, CronJobEntity> DeadNode { get; }

        public IJobPersistenceProvider<TimeJobEntity, CronJobEntity> LiveNode => DeadNode;

        public Task LapseLeasesAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
        {
            _clock.Advance(_LeaseDuration + TimeSpan.FromSeconds(1));
            return Task.CompletedTask;
        }
    }
}
