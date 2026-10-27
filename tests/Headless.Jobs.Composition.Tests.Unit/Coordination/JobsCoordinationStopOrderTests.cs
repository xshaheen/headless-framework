// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.Context;
using Headless.Coordination;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Tests.Coordination;

/// <summary>
/// A drain participant completes inside its own service's stop, as the Jobs scheduler does. The heartbeat must leave
/// only after that, whichever service the host stops first: <c>UsePostgreSql</c> registers coordination after the
/// scheduler, so a sequential stop reaches the heartbeat first.
/// </summary>
public sealed class JobsCoordinationStopOrderTests : TestBase
{
    private static readonly TimeSpan _ShutdownTimeout = TimeSpan.FromSeconds(30);

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public async Task should_leave_after_the_participant_drains_without_waiting_out_the_stop_timeout(
        bool coordinationRegisteredLast,
        bool stopConcurrently
    )
    {
        // given
        var events = new ConcurrentQueue<string>();
        var store = new RecordingMembershipStore(events);
        var draining = new DrainingService(events);
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Services.Configure<HostOptions>(options =>
        {
            options.ShutdownTimeout = _ShutdownTimeout;
            options.ServicesStopConcurrently = stopConcurrently;
        });
        builder.Services.AddSingleton(store);
        builder.Services.AddHeadlessHostIdentity(identity => identity.HostName = "stopping-node");
        builder.Services.AddSingleton<INodeDrainParticipant>(draining);

        if (coordinationRegisteredLast)
        {
            builder.Services.AddHostedService(_ => draining);
            builder.Services.AddCoordinationCore<RecordingMembershipStore>(_ => { });
        }
        else
        {
            builder.Services.AddCoordinationCore<RecordingMembershipStore>(_ => { });
            builder.Services.AddHostedService(_ => draining);
        }

        using var host = builder.Build();
        await host.StartAsync(AbortToken);
        await store.Registered.WaitAsync(TimeSpan.FromSeconds(10), AbortToken);
        var started = TimeProvider.System.GetTimestamp();

        // when
        await host.StopAsync(AbortToken);

        // then: drained first, then left once, well inside the stop budget
        TimeProvider.System.GetElapsedTime(started).Should().BeLessThan(TimeSpan.FromSeconds(10));
        events.Should().Equal("drained", "left");
    }

    /// <summary>Completes its drain only from its own <c>StopAsync</c>, like the Jobs scheduler.</summary>
    private sealed class DrainingService(ConcurrentQueue<string> events) : IHostedService, INodeDrainParticipant
    {
        private readonly TaskCompletionSource _drained = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task StartAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            events.Enqueue("drained");
            _drained.TrySetResult();

            return Task.CompletedTask;
        }

        public Task WaitUntilDrainedAsync(CancellationToken cancellationToken)
        {
            return _drained.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class RecordingMembershipStore(ConcurrentQueue<string> events) : IMembershipStore
    {
        private readonly TaskCompletionSource _registered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private long _generation;

        public Task Registered => _registered.Task;

        public ValueTask<NodeIncarnation> AllocateIncarnationAsync(
            NodeId nodeId,
            CancellationToken cancellationToken = default
        )
        {
            return ValueTask.FromResult(new NodeIncarnation(Interlocked.Increment(ref _generation)));
        }

        public ValueTask<bool> UpsertDescriptorAsync(
            NodeDescriptor descriptor,
            CancellationToken cancellationToken = default
        )
        {
            _registered.TrySetResult();

            return ValueTask.FromResult(true);
        }

        public ValueTask<bool> HeartbeatAsync(NodeIdentity identity, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(true);
        }

        public ValueTask LeaveAsync(NodeIdentity identity, CancellationToken cancellationToken = default)
        {
            events.Enqueue("left");

            return ValueTask.CompletedTask;
        }

        public ValueTask<IReadOnlyList<NodeLivenessSnapshot>> ReadLivenessAsync(
            CancellationToken cancellationToken = default
        )
        {
            return ValueTask.FromResult<IReadOnlyList<NodeLivenessSnapshot>>([]);
        }

        public ValueTask<NodeLivenessState?> ReadNodeLivenessAsync(
            NodeIdentity identity,
            CancellationToken cancellationToken = default
        )
        {
            return ValueTask.FromResult<NodeLivenessState?>(null);
        }

        public ValueTask<IReadOnlyList<NodeIdentity>> ReadLiveNodesAsync(CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult<IReadOnlyList<NodeIdentity>>([]);
        }
    }
}
