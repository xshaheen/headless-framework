// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Context;
using Headless.Coordination;
using Headless.Jobs.Coordination;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace Tests.Coordination;

/// <summary>
/// The Jobs startup gate registers the node in <c>StartingAsync</c>. A later hosted service whose <c>StartingAsync</c>
/// runs past <see cref="CoordinationOptions.DeadThreshold"/> must not leave the node unbeaten for that long, or the
/// store expires the registration and the first beat is refused, fail-stopping the host on every start.
/// </summary>
public sealed class JobsCoordinationSlowStartupTests : TestBase
{
    [Fact]
    public async Task should_keep_membership_through_a_starting_async_longer_than_the_dead_threshold()
    {
        // given
        var timeProvider = new FakeTimeProvider();
        var options = new CoordinationOptions();
        var store = new ClockedMembershipStore(timeProvider, options.DeadThreshold);
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton<TimeProvider>(timeProvider);
        builder.Services.AddSingleton(store);
        builder.Services.AddHeadlessHostIdentity(identity => identity.HostName = "slow-node");
        builder.Services.AddCoordinationCore<ClockedMembershipStore>(_ => { });
        builder.Services.AddHostedService<JobsCoordinationStartupGate>();
        builder.Services.AddHostedService(_ => new SlowStartingService(
            timeProvider,
            options.DeadThreshold + TimeSpan.FromSeconds(10)
        ));

        using var host = builder.Build();
        var membership = host.Services.GetRequiredService<INodeMembership>();
        var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();

        // when
        await host.StartAsync(AbortToken);

        try
        {
            // then: the loop beat throughout the 40s StartingAsync, so the store never expired the registration
            membership.LocalMembershipLostToken.IsCancellationRequested.Should().BeFalse();
            membership.Identity.Should().NotBeNull();
            lifetime.ApplicationStopping.IsCancellationRequested.Should().BeFalse();
            store.RefusedBeats.Should().Be(0);
            store.AcceptedBeats.Should().BePositive();
        }
        finally
        {
            await host.StopAsync(AbortToken);
        }
    }

    /// <summary>Steps the app clock in small increments, yielding so the heartbeat loop runs between them.</summary>
    private sealed class SlowStartingService(FakeTimeProvider timeProvider, TimeSpan duration) : IHostedLifecycleService
    {
        public async Task StartingAsync(CancellationToken cancellationToken)
        {
            var step = TimeSpan.FromMilliseconds(500);

            for (var elapsed = TimeSpan.Zero; elapsed < duration; elapsed += step)
            {
                timeProvider.Advance(step);
                await Task.Delay(5, cancellationToken);
            }
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task StartedAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task StoppingAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task StoppedAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// A single-node store that refuses a beat once the last one is <c>DeadThreshold</c> old on the store clock, as
    /// the relational and Redis stores do.
    /// </summary>
    private sealed class ClockedMembershipStore(TimeProvider clock, TimeSpan deadThreshold) : IMembershipStore
    {
        private readonly Lock _gate = new();
        private long _generation;
        private DateTimeOffset? _lastBeat;

        public int AcceptedBeats { get; private set; }

        public int RefusedBeats { get; private set; }

        public ValueTask<NodeIncarnation> AllocateIncarnationAsync(
            NodeId nodeId,
            CancellationToken cancellationToken = default
        )
        {
            lock (_gate)
            {
                return ValueTask.FromResult(new NodeIncarnation(++_generation));
            }
        }

        public ValueTask<bool> UpsertDescriptorAsync(
            NodeDescriptor descriptor,
            CancellationToken cancellationToken = default
        )
        {
            lock (_gate)
            {
                if (descriptor.Identity.Incarnation.Value != _generation)
                {
                    return ValueTask.FromResult(false);
                }

                _lastBeat = clock.GetUtcNow();

                return ValueTask.FromResult(true);
            }
        }

        public ValueTask<bool> HeartbeatAsync(NodeIdentity identity, CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                var now = clock.GetUtcNow();

                if (_lastBeat is not { } last || now - last >= deadThreshold)
                {
                    RefusedBeats++;

                    return ValueTask.FromResult(false);
                }

                _lastBeat = now;
                AcceptedBeats++;

                return ValueTask.FromResult(true);
            }
        }

        public ValueTask LeaveAsync(NodeIdentity identity, CancellationToken cancellationToken = default)
        {
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
