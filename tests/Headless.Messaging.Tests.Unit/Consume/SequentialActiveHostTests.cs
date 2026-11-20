// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.DistributedLocks;
using Headless.Messaging;
using Headless.Messaging.Internal;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tests.Guarantees;
using Tests.Helpers;
using Tests.Simulation;

namespace Tests.Consume;

/// <summary>
/// Two hosts of one deployment run a sequential consumer on a transport that neither routes by key nor offers a single
/// active consumer, which the simulated broker is. With a messaging lock, only the host that holds it listens, and the
/// other takes over when the holder stops or loses it; without one, both listen.
/// </summary>
public sealed class SequentialActiveHostTests : TestBase
{
    private const string _IntakeResourceMarker = "sequential-intake";

    [Fact]
    [Guarantee("MSG-LANE-08")]
    public async Task should_listen_on_only_the_host_that_holds_the_lock()
    {
        // given
        var broker = new SimulatedBroker(new SimulationClock());
        await using var locks = _CreateLocks(broker.Clock);
        var (holderProbe, holderOrder) = _RecordingProbe();
        var (otherProbe, _) = _RecordingProbe();
        await using var holder = await PipelineHosts.StartAsync(
            holderProbe,
            AbortToken,
            _Sequential,
            broker,
            services => _UseLock(services, locks)
        );
        await using var other = await PipelineHosts.StartBesideAsync(
            holder,
            otherProbe,
            AbortToken,
            _Sequential,
            services => _UseLock(services, locks)
        );
        var ids = Enumerable.Range(0, 10).Select(static i => $"a-{i}").ToArray();

        // when
        foreach (var id in ids)
        {
            await _EnqueueAsync(other, id);
        }

        // then
        broker.ClientsOf(PipelineOrder.Name).Should().ContainSingle();
        await holder.WithinBoundAsync(holderProbe.WhenCompletedAsync(ids[^1], 1), AbortToken);
        holderOrder.Should().Equal(ids);
        ids.Should().OnlyContain(id => otherProbe.Attempts(id) == 0);
        (await _IntakeLocksAsync(locks)).Should().ContainSingle();
    }

    [Fact]
    [Guarantee("MSG-LANE-08")]
    public async Task should_take_over_the_backlog_in_publish_order_when_the_holder_stops()
    {
        // given
        var broker = new SimulatedBroker(new SimulationClock());
        await using var locks = _CreateLocks(broker.Clock);
        var (holderProbe, _) = _RecordingProbe();
        var (otherProbe, otherOrder) = _RecordingProbe();
        await using var holder = await PipelineHosts.StartAsync(
            holderProbe,
            AbortToken,
            _Sequential,
            broker,
            services => _UseLock(services, locks)
        );
        await using var other = await PipelineHosts.StartBesideAsync(
            holder,
            otherProbe,
            AbortToken,
            _Sequential,
            services => _UseLock(services, locks)
        );
        broker.ClientsOf(PipelineOrder.Name).Should().ContainSingle();

        // when: the holder stops, and a backlog builds while nobody listens
        await holder.StopAsync(AbortToken);
        broker.ClientsOf(PipelineOrder.Name).Should().BeEmpty();
        (await _IntakeLocksAsync(locks)).Should().BeEmpty("a holder that stops releases the lock");
        var ids = Enumerable.Range(0, 10).Select(static i => $"a-{i}").ToArray();
        foreach (var id in ids)
        {
            await _EnqueueAsync(other, id);
        }

        // then: the other host takes the lock on its next try and runs the backlog in publish order
        other.Advance(ConsumerRegister.ActiveHostRetryInterval);
        await other.WithinBoundAsync(otherProbe.WhenCompletedAsync(ids[^1], 1), AbortToken);
        otherOrder.Should().Equal(ids);
        ids.Should().OnlyContain(id => holderProbe.Attempts(id) == 0);
        broker.ClientsOf(PipelineOrder.Name).Should().ContainSingle();
    }

    [Fact]
    [Guarantee("MSG-LANE-08")]
    public async Task should_stop_listening_and_compete_again_when_the_holder_loses_the_lock()
    {
        // given
        var broker = new SimulatedBroker(new SimulationClock());
        await using var locks = _CreateLocks(broker.Clock);
        var lockProvider = new LosableLock(locks.GetRequiredService<IDistributedLock>());
        var firstProbe = new PipelineProbe();
        var secondProbe = new PipelineProbe();
        await using var first = await PipelineHosts.StartAsync(
            firstProbe,
            AbortToken,
            _Sequential,
            broker,
            services => _UseLock(services, lockProvider)
        );
        await using var second = await PipelineHosts.StartBesideAsync(
            first,
            secondProbe,
            AbortToken,
            _Sequential,
            services => _UseLock(services, lockProvider)
        );
        var intake = (await _IntakeLocksAsync(locks)).Should().ContainSingle().Subject;
        var closed = _NextClientClosedAsync(broker);
        var mark = broker.Clock.WaitMark;

        // when: the holder's lease expires in the store and another process takes the lock before the holder renews
        await using var thief = await lockProvider.LoseAsync(intake.Resource);

        // then: the holder stops taking deliveries in, and its own next try finds the lock taken
        await first.WithinBoundAsync(closed, AbortToken);
        broker.ClientsOf(PipelineOrder.Name).Should().BeEmpty();
        await first.WhenWaitArmedAsync(mark, ConsumerRegister.ActiveHostRetryInterval, AbortToken);
        broker.ClientsOf(PipelineOrder.Name).Should().BeEmpty();

        // and: once the lock is free again, a host takes the subscription back on its next try
        await _EnqueueAsync(second, "a-1");
        await thief.ReleaseAsync();
        first.Advance(ConsumerRegister.ActiveHostRetryInterval);
        await first.WithinBoundAsync(
            Task.WhenAny(firstProbe.WhenCompletedAsync("a-1", 1), secondProbe.WhenCompletedAsync("a-1", 1)),
            AbortToken
        );
        (firstProbe.Attempts("a-1") + secondProbe.Attempts("a-1")).Should().Be(1);
        broker.ClientsOf(PipelineOrder.Name).Should().ContainSingle();
    }

    [Fact]
    public async Task should_record_the_lease_on_the_holder_only_and_forget_it_when_the_holder_stops()
    {
        // given
        var broker = new SimulatedBroker(new SimulationClock());
        await using var locks = _CreateLocks(broker.Clock);
        await using var holder = await PipelineHosts.StartAsync(
            new PipelineProbe(),
            AbortToken,
            _Sequential,
            broker,
            services => _UseLock(services, locks)
        );
        await using var other = await PipelineHosts.StartBesideAsync(
            holder,
            new PipelineProbe(),
            AbortToken,
            _Sequential,
            services => _UseLock(services, locks)
        );
        var intake = (await _IntakeLocksAsync(locks)).Should().ContainSingle().Subject;

        // then: the dashboard can name the holder from the lease the lock store reports
        holder
            .Services.GetRequiredService<SequentialIntakeHolds>()
            .LeaseOf(intake.Resource)
            .Should()
            .Be(intake.LeaseId);
        other.Services.GetRequiredService<SequentialIntakeHolds>().LeaseOf(intake.Resource).Should().BeNull();

        // when
        await holder.StopAsync(AbortToken);

        // then
        holder.Services.GetRequiredService<SequentialIntakeHolds>().LeaseOf(intake.Resource).Should().BeNull();
    }

    [Fact]
    public async Task should_listen_on_every_host_and_warn_when_no_lock_is_configured()
    {
        // given
        var broker = new SimulatedBroker(new SimulationClock());
        var firstLog = new List<(LogLevel Level, EventId EventId, string Message)>();
        var secondLog = new List<(LogLevel Level, EventId EventId, string Message)>();

        // when
        await using var first = await PipelineHosts.StartAsync(
            new PipelineProbe(),
            AbortToken,
            _Sequential,
            broker,
            services => services.AddSingleton<ILoggerProvider>(new CapturingLoggerProvider(firstLog))
        );
        await using var second = await PipelineHosts.StartBesideAsync(
            first,
            new PipelineProbe(),
            AbortToken,
            _Sequential,
            services => services.AddSingleton<ILoggerProvider>(new CapturingLoggerProvider(secondLog))
        );

        // then
        broker.ClientsOf(PipelineOrder.Name).Should().HaveCount(2);
        _UnguardedWarnings(firstLog).Should().ContainSingle();
        _UnguardedWarnings(secondLog).Should().ContainSingle();
    }

    [Fact]
    public async Task should_take_no_lock_for_a_consumer_that_is_not_sequential()
    {
        // given
        var broker = new SimulatedBroker(new SimulationClock());
        await using var locks = _CreateLocks(broker.Clock);

        // when
        await using var first = await PipelineHosts.StartAsync(
            new PipelineProbe(),
            AbortToken,
            configure: null,
            broker,
            services => _UseLock(services, locks)
        );
        await using var second = await PipelineHosts.StartBesideAsync(
            first,
            new PipelineProbe(),
            AbortToken,
            configure: null,
            services => _UseLock(services, locks)
        );

        // then
        broker.ClientsOf(PipelineOrder.Name).Should().HaveCount(2);
        (await _IntakeLocksAsync(locks)).Should().BeEmpty();
    }

    // Retry pickup polls rarely, so moving the clock releases only lock renewals and lock retries.
    private static void _Sequential(MessagingSetupBuilder setup)
    {
        PipelineHosts.QuietRetryPickup(setup);
        setup.Tune(PipelineOrderConsumer.Identity, consumer => consumer.Concurrency(4).Sequential());
    }

    // One lock store for both hosts, as one Redis or database serves every process, on the simulation clock so its
    // leases expire and renew only when the test moves time.
    private static ServiceProvider _CreateLocks(SimulationClock clock)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(clock);
        services.AddHeadlessDistributedLocks(setup => setup.UseInMemory());

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    // Registered before messaging, whose own registration only fills the key when nothing holds it.
    private static void _UseLock(IServiceCollection services, ServiceProvider locks) =>
        _UseLock(services, locks.GetRequiredService<IDistributedLock>());

    private static void _UseLock(IServiceCollection services, IDistributedLock lockProvider) =>
        services.AddKeyedSingleton(MessagingKeys.LockProvider, lockProvider);

    private static async Task<IReadOnlyList<DistributedLockInfo>> _IntakeLocksAsync(ServiceProvider locks)
    {
        var active = await locks.GetRequiredService<IDistributedLock>().ListActiveLocksAsync(AbortToken);

        return [.. active.Where(static x => x.Resource.Contains(_IntakeResourceMarker, StringComparison.Ordinal))];
    }

    private static IEnumerable<(LogLevel Level, EventId EventId, string Message)> _UnguardedWarnings(
        List<(LogLevel Level, EventId EventId, string Message)> log
    )
    {
        lock (log)
        {
            return [.. log.Where(static e => e.Level == LogLevel.Warning && e.EventId.Id == 4142)];
        }
    }

    // Startup verification opens and closes clients of its own, so only a close after this call counts.
    private static Task _NextClientClosedAsync(SimulatedBroker broker)
    {
        static bool isClose(BrokerEvent e) =>
            e.Kind is BrokerEventKind.ClientClosed
            && string.Equals(e.Subscription, PipelineOrder.Name, StringComparison.Ordinal);

        return broker.WaitForEventsAsync(isClose, broker.Events.Count(isClose) + 1, AbortToken);
    }

    private static (PipelineProbe Probe, ConcurrentQueue<string> Order) _RecordingProbe()
    {
        var order = new ConcurrentQueue<string>();
        var probe = new PipelineProbe
        {
            Behavior = (id, _, _) =>
            {
                order.Enqueue(id);
                return ValueTask.CompletedTask;
            },
        };

        return (probe, order);
    }

    private static Task _EnqueueAsync(SimulationHost host, string id) =>
        host.EnqueueAsync(id, new QueueOptions { RoutingAffinityKey = "a" }, AbortToken);

    /// <summary>
    /// Passes every call to the shared lock, and lets a test take a lease from its holder the way an expired lease in a
    /// real store is: the store forgets the holder, another process acquires the lock, and the holder's lease reports
    /// the loss. The in-memory store cannot expire a lease the holder keeps renewing, so this stands in for it.
    /// </summary>
    private sealed class LosableLock(IDistributedLock inner) : IDistributedLock
    {
        private readonly ConcurrentDictionary<string, LosableLease> _leases = new(StringComparer.Ordinal);

        public TimeProvider TimeProvider => inner.TimeProvider;

        public ILogger Logger => inner.Logger;

        public TimeSpan DefaultTimeUntilExpires => inner.DefaultTimeUntilExpires;

        public TimeSpan DefaultAcquireTimeout => inner.DefaultAcquireTimeout;

        public async Task<IDistributedLease> LoseAsync(string resource)
        {
#pragma warning disable CA2000 // False positive: the host that acquired the lease owns it and disposes it.
            _leases.TryRemove(resource, out var held).Should().BeTrue("a host holds the lock");
#pragma warning restore CA2000
            await held!.Inner.ReleaseAsync();
            var thief = await inner.TryAcquireAsync(
                resource,
                new DistributedLockAcquireOptions { TimeUntilExpires = TimeSpan.FromHours(1) },
                AbortToken
            );
            thief.Should().NotBeNull();
            await held.LoseAsync();

            return thief!;
        }

        public async Task<IDistributedLease> AcquireAsync(
            string resource,
            DistributedLockAcquireOptions? options = null,
            CancellationToken cancellationToken = default
        ) => _Track(await inner.AcquireAsync(resource, options, cancellationToken));

        public async Task<IDistributedLease?> TryAcquireAsync(
            string resource,
            DistributedLockAcquireOptions? options = null,
            CancellationToken cancellationToken = default
        )
        {
            var lease = await inner.TryAcquireAsync(resource, options, cancellationToken);

            return lease is null ? null : _Track(lease);
        }

        public Task<bool> RenewAsync(
            string resource,
            string leaseId,
            TimeSpan? timeUntilExpires = null,
            CancellationToken cancellationToken = default
        ) => inner.RenewAsync(resource, leaseId, timeUntilExpires, cancellationToken);

        public Task<string?> GetLeaseIdAsync(string resource, CancellationToken cancellationToken = default) =>
            inner.GetLeaseIdAsync(resource, cancellationToken);

        public Task ReleaseAsync(string resource, string leaseId, CancellationToken cancellationToken = default) =>
            inner.ReleaseAsync(resource, leaseId, cancellationToken);

        public Task<bool> IsLockedAsync(string resource, CancellationToken cancellationToken = default) =>
            inner.IsLockedAsync(resource, cancellationToken);

        public Task<TimeSpan?> GetExpirationAsync(string resource, CancellationToken cancellationToken = default) =>
            inner.GetExpirationAsync(resource, cancellationToken);

        public Task<DistributedLockInfo?> GetLockInfoAsync(
            string resource,
            CancellationToken cancellationToken = default
        ) => inner.GetLockInfoAsync(resource, cancellationToken);

        public Task<IReadOnlyList<DistributedLockInfo>> ListActiveLocksAsync(
            CancellationToken cancellationToken = default
        ) => inner.ListActiveLocksAsync(cancellationToken);

        public Task<long> GetActiveLocksCountAsync(CancellationToken cancellationToken = default) =>
            inner.GetActiveLocksCountAsync(cancellationToken);

        private LosableLease _Track(IDistributedLease lease)
        {
            var losable = new LosableLease(lease);
            _leases[lease.Resource] = losable;

            return losable;
        }
    }

    private sealed class LosableLease : IDistributedLease
    {
        private readonly CancellationTokenSource _lost;

        public LosableLease(IDistributedLease inner)
        {
            Inner = inner;
            _lost = CancellationTokenSource.CreateLinkedTokenSource(inner.LostToken);
            // Captured once, as a real lease does, so reading it after disposal does not throw.
            LostToken = _lost.Token;
        }

        public IDistributedLease Inner { get; }

        public string LeaseId => Inner.LeaseId;

        public LockFencingToken? FencingToken => Inner.FencingToken;

        public string Resource => Inner.Resource;

        public int RenewalCount => Inner.RenewalCount;

        public DateTimeOffset AcquiredAt => Inner.AcquiredAt;

        public TimeSpan TimeWaitedForLock => Inner.TimeWaitedForLock;

        public CancellationToken LostToken { get; }

        public bool CanObserveLoss => true;

        public Task LoseAsync() => _lost.CancelAsync();

        public Task ReleaseAsync() => Inner.ReleaseAsync();

        public Task<bool> RenewAsync(
            TimeSpan? timeUntilExpires = null,
            CancellationToken cancellationToken = default
        ) => Inner.RenewAsync(timeUntilExpires, cancellationToken);

        public async ValueTask DisposeAsync()
        {
            await Inner.DisposeAsync();
            _lost.Dispose();
        }
    }
}
