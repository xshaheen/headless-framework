// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using Headless.Messaging;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Tests.Capabilities;
using Tests.Guarantees;
using MessagingHeaders = Headless.Messaging.Headers;

namespace Tests;

/// <summary>A broker-delivered message and the provider's opaque settlement value.</summary>
[PublicAPI]
public sealed record TransportConformanceDelivery(TransportMessage Message, object? SettlementValue);

/// <summary>What a conformance scenario asks a provider leaf to configure on one session beyond its defaults.</summary>
[PublicAPI]
public sealed record TransportConformanceSessionOptions
{
    /// <summary>The most deliveries the consumer may hand to the session at once.</summary>
    public byte Concurrency { get; init; } = 1;

    /// <summary>
    /// Stands in for an application's custom headers builder. The leaf installs the provider's builder, which calls this
    /// with the message id the native message carries, so a throw from it is a builder failure.
    /// </summary>
    public Action<string?>? HeaderBuilder { get; init; }

    /// <summary>
    /// How many times the broker may hand one message over before it gives up on it, or <see langword="null"/> for the
    /// provider default.
    /// </summary>
    public int? DeliveryLimit { get; init; }

    /// <summary>Whether a message the broker gives up on is kept in a dead-letter destination the leaf can read.</summary>
    public bool DeadLettering { get; init; }

    /// <summary>How long the broker keeps an unconsumed message before expiring it, or <see langword="null"/> for never.</summary>
    public TimeSpan? TimeToLive { get; init; }

    /// <summary>Whether the destination orders the messages of one routing affinity key.</summary>
    public bool RoutingAffinity { get; init; }
}

/// <summary>Owns one isolated producer/consumer pair used by broker-backed conformance tests.</summary>
/// <remarks>
/// Every receive timeout, no-redelivery window, and publish retry pause runs on <see cref="TimeProvider"/>: the system
/// clock for a real broker, or a simulator's clock, which moves only while the test waits, so the same test body runs
/// against both.
/// </remarks>
[PublicAPI]
public sealed class TransportConsumerConformanceSession(
    string destination,
    ITransport producer,
    IConsumerClient consumer,
    TimeSpan noRedeliveryWindow,
    Func<ValueTask>? disposeProviderResources = null,
    TimeSpan? listeningTimeout = null,
    Func<CancellationToken, ValueTask<TransportConsumerConformanceSession>>? createReplacementSession = null,
    TimeProvider? timeProvider = null
) : IAsyncDisposable
{
    private readonly ConcurrentQueue<LogMessageEventArgs> _logs = new();
    private readonly DeliveryInbox _inbox = new(timeProvider ?? TimeProvider.System);

    private readonly TimeSpan _listeningTimeout = listeningTimeout ?? TimeSpan.FromSeconds(2);
    private CancellationTokenSource? _listeningCts;
    private Task? _listeningTask;
    private int _stopped;
    private int _disposed;

    public string Destination { get; } = destination;

    public TimeSpan NoRedeliveryWindow { get; } = noRedeliveryWindow;

    /// <summary>Gets the clock the session's timeouts and windows run on; the system clock unless the session was given one.</summary>
    public TimeProvider TimeProvider { get; } = timeProvider ?? TimeProvider.System;

    public IConsumerClient Consumer { get; } = consumer;

    public async Task StartAsync(
        Func<TransportConformanceDelivery, Task>? onDelivery = null,
        Action<LogMessageEventArgs>? onLog = null,
        CancellationToken cancellationToken = default
    )
    {
        if (_listeningTask is not null)
        {
            throw new InvalidOperationException("The conformance session has already started.");
        }

        var inbox = _inbox;
        Consumer.AttachCallbacks(
            onMessage: async (message, settlementValue) =>
            {
                var delivery = new TransportConformanceDelivery(message, settlementValue);
                inbox.Add(delivery);

                if (onDelivery is not null)
                {
                    await onDelivery(delivery).ConfigureAwait(false);
                }
            },
            onLog: log =>
            {
                _logs.Enqueue(log);
                onLog?.Invoke(log);
            }
        );

        _listeningCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _listeningTask = Task.Run(
            async () => await Consumer.ListeningAsync(_listeningTimeout, _listeningCts.Token),
            CancellationToken.None
        );

        using var readinessCts = TimeSpan.FromSeconds(10).ToCancellationTokenSource(cancellationToken);
        await Consumer.WaitUntilReadyAsync(readinessCts.Token).ConfigureAwait(false);
    }

    public Task<OperateResult> PublishAsync(TransportMessage message, CancellationToken cancellationToken = default)
    {
        return producer.SendAsync(message, cancellationToken);
    }

    /// <summary>
    /// Publishes until the producer succeeds. The first publishes after a connection loss can fail while the client
    /// reconnects, and the outbox retries them, so only a publish that never succeeds within the window is a defect.
    /// </summary>
    public async Task PublishUntilSucceededAsync(
        TransportMessage message,
        TimeSpan window,
        CancellationToken cancellationToken = default
    )
    {
        using var cts = new SessionTimeout(TimeProvider, window, cancellationToken);
        Exception? lastFailure = null;

        while (!cts.IsCancellationRequested)
        {
            try
            {
                var result = await producer.SendAsync(message, cts.Token).ConfigureAwait(false);
                if (result.Succeeded)
                {
                    return;
                }

                lastFailure = result.Exception;
            }
#pragma warning disable CA1031 // Any publish failure during the reconnect window is retried; the window bounds it.
            catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
            {
                lastFailure = ex;
            }

            try
            {
                await TimeProvider.Delay(TimeSpan.FromMilliseconds(500), cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                break;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        throw new TimeoutException($"The producer did not recover within {window}.", lastFailure);
    }

    public ValueTask<TransportConsumerConformanceSession> CreateReplacementAsync(
        CancellationToken cancellationToken = default
    )
    {
        return createReplacementSession is not null
            ? createReplacementSession(cancellationToken)
            : throw new InvalidOperationException("The conformance session does not provide a replacement consumer.");
    }

    public async Task<TransportConformanceDelivery> ReceiveAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default
    )
    {
        var delivery = await _inbox.TakeAsync(timeout, cancellationToken).ConfigureAwait(false);
        if (delivery is not null)
        {
            return delivery;
        }

        var diagnostics = string.Join(Environment.NewLine, _logs.Select(log => $"{log.LogType}: {log.Reason}"));
        throw new TimeoutException(
            $"No broker delivery arrived within {timeout}. Consumer diagnostics:{Environment.NewLine}{diagnostics}"
        );
    }

    public async Task<bool> RemainsEmptyAsync(TimeSpan window, CancellationToken cancellationToken = default)
    {
        return await _inbox.TakeAsync(window, cancellationToken).ConfigureAwait(false) is null;
    }

    public async Task StopAsync(TimeSpan timeout)
    {
        if (Interlocked.Exchange(ref _stopped, 1) != 0)
        {
            return;
        }

        if (_listeningCts is not null)
        {
            await _listeningCts.CancelAsync().ConfigureAwait(false);
        }

        await Consumer
            .ShutdownAsync(timeout, CancellationToken.None)
            .AsTask()
            .WaitAsync(timeout + TimeSpan.FromSeconds(1))
            .ConfigureAwait(false);

        if (_listeningTask is not null)
        {
            try
            {
                await _listeningTask.WaitAsync(timeout + TimeSpan.FromSeconds(1)).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_listeningCts?.IsCancellationRequested == true)
            {
                // Expected when this session stops its listener.
            }
            catch (TimeoutException ex)
            {
                var diagnostics = string.Join(Environment.NewLine, _logs.Select(log => $"{log.LogType}: {log.Reason}"));
                throw new TimeoutException(
                    $"Consumer listener did not stop within {timeout}. "
                        + $"Cancellation requested: {_listeningCts?.IsCancellationRequested}; task status: {_listeningTask.Status}. "
                        + $"Diagnostics:{Environment.NewLine}{diagnostics}",
                    ex
                );
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            await StopAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        }
        finally
        {
            try
            {
                await Consumer.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                try
                {
                    await producer.DisposeAsync().ConfigureAwait(false);
                }
                finally
                {
                    try
                    {
                        if (disposeProviderResources is not null)
                        {
                            await disposeProviderResources().ConfigureAwait(false);
                        }
                    }
                    finally
                    {
                        _listeningCts?.Dispose();
                    }
                }
            }
        }
    }

    /// <summary>
    /// Cancels when a timeout on the session clock runs out or the caller cancels. A zero timeout is already over and a
    /// negative one never runs out, as with <c>TimeSpan.ToCancellationTokenSource</c>.
    /// </summary>
    private sealed class SessionTimeout : IDisposable
    {
        private readonly CancellationTokenSource _timeout;
        private readonly CancellationTokenSource _linked;

        public SessionTimeout(TimeProvider clock, TimeSpan timeout, CancellationToken cancellationToken)
        {
            _timeout =
                timeout > TimeSpan.Zero ? clock.CreateCancellationTokenSource(timeout) : new CancellationTokenSource();
            if (timeout == TimeSpan.Zero)
            {
                _timeout.Cancel();
            }

            _linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _timeout.Token);
        }

        public CancellationToken Token => _linked.Token;

        public bool IsCancellationRequested => _linked.IsCancellationRequested;

        public void Dispose()
        {
            _linked.Dispose();
            _timeout.Dispose();
        }
    }

    /// <summary>
    /// Holds deliveries until the test takes them. A delivery that arrives while the test waits ends that wait on the
    /// delivering thread, before the provider's callback returns, so the wait's timeout is gone by the time a simulated
    /// clock could see the consumer idle and fire it.
    /// </summary>
    private sealed class DeliveryInbox(TimeProvider clock)
    {
        private readonly Lock _gate = new();
        private readonly Queue<TransportConformanceDelivery> _queued = new();
        private readonly List<Waiter> _waiters = [];

        public void Add(TransportConformanceDelivery delivery)
        {
            Waiter waiter;
            lock (_gate)
            {
                if (_waiters.Count == 0)
                {
                    _queued.Enqueue(delivery);
                    return;
                }

                waiter = _waiters[0];
                _waiters.RemoveAt(0);
            }

            waiter.End(delivery);
        }

        /// <summary>Takes the next delivery, or <see langword="null"/> when none arrives within <paramref name="timeout"/>.</summary>
        /// <param name="timeout">How long to wait; zero does not wait, and a negative value waits until canceled.</param>
        /// <param name="cancellationToken">Abandons the wait.</param>
        public Task<TransportConformanceDelivery?> TakeAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            Waiter waiter;
            lock (_gate)
            {
                if (_queued.TryDequeue(out var queued))
                {
                    return Task.FromResult<TransportConformanceDelivery?>(queued);
                }

                cancellationToken.ThrowIfCancellationRequested();
                if (timeout == TimeSpan.Zero)
                {
                    return Task.FromResult<TransportConformanceDelivery?>(null);
                }

                waiter = new Waiter(this);
                _waiters.Add(waiter);
            }

            waiter.Arm(clock, timeout, cancellationToken);
            return waiter.Task;
        }

        private bool _TryWithdraw(Waiter waiter)
        {
            lock (_gate)
            {
                return _waiters.Remove(waiter);
            }
        }

        private sealed class Waiter(DeliveryInbox inbox)
        {
            private readonly TaskCompletionSource<TransportConformanceDelivery?> _result = new(
                TaskCreationOptions.RunContinuationsAsynchronously
            );

            private readonly Lock _gate = new();
            private ITimer? _timer;
            private CancellationTokenRegistration _cancellation;
            private bool _ended;

            public Task<TransportConformanceDelivery?> Task => _result.Task;

            public void Arm(TimeProvider clock, TimeSpan timeout, CancellationToken cancellationToken)
            {
                // Either may end the wait before Arm returns: a short timer can fire inside CreateTimer, and a canceled
                // token runs its callback inside Register. _Release then finds nothing to release, so it is done here.
                var timer =
                    timeout > TimeSpan.Zero
                        ? clock.CreateTimer(
                            static self => ((Waiter)self!)._OnTimeout(),
                            this,
                            timeout,
                            Timeout.InfiniteTimeSpan
                        )
                        : null;
                var cancellation = cancellationToken.Register(
                    static (self, token) => ((Waiter)self!)._OnCanceled(token),
                    this
                );

                lock (_gate)
                {
                    if (!_ended)
                    {
                        _timer = timer;
                        _cancellation = cancellation;
                        return;
                    }
                }

                timer?.Dispose();
                cancellation.Dispose();
            }

            public void End(TransportConformanceDelivery delivery)
            {
                _Release();
                _result.TrySetResult(delivery);
            }

            private void _OnTimeout()
            {
                if (inbox._TryWithdraw(this))
                {
                    _Release();
                    _result.TrySetResult(null);
                }
            }

            private void _OnCanceled(CancellationToken cancellationToken)
            {
                if (inbox._TryWithdraw(this))
                {
                    _Release();
                    _result.TrySetCanceled(cancellationToken);
                }
            }

            private void _Release()
            {
                ITimer? timer;
                CancellationTokenRegistration cancellation;
                lock (_gate)
                {
                    _ended = true;
                    timer = _timer;
                    cancellation = _cancellation;
                    _timer = null;
                    _cancellation = default;
                }

                timer?.Dispose();
                cancellation.Dispose();
            }
        }
    }
}

/// <summary>
/// Provider wiring shared by broker-observed conformance suites: the manifest name and an isolated session factory.
/// Suites derive from this rather than from each other so that a suite inherits only the cases it means to run.
/// </summary>
[PublicAPI]
public abstract class TransportConsumerConformanceSessionTestsBase : TestBase
{
    protected abstract string ProviderName { get; }

    protected abstract ValueTask<TransportConsumerConformanceSession> CreateSessionAsync(
        CancellationToken cancellationToken
    );

    protected void RequireSupport(TransportConformanceScenario scenario)
    {
        if (!TransportConformanceManifest.Providers.TryGetValue(ProviderName, out var profile))
        {
            throw new InvalidOperationException($"{ProviderName} is not registered in the conformance manifest.");
        }

        var support = profile.Scenarios[scenario];
        if (support.Status == ConformanceStatus.Supported)
        {
            return;
        }

        var evidence = support.IssueUrl is null ? support.Rationale : $"{support.Rationale} {support.IssueUrl}";
        Assert.Skip($"{ProviderName} {scenario}: {support.Status}. {evidence}");
    }
}

/// <summary>Shared broker-observed transport, delivery, and settlement invariants.</summary>
[PublicAPI]
public abstract class TransportConsumerConformanceTestsBase : TransportConsumerConformanceSessionTestsBase
{
    protected virtual void ConfigureTransport(MessagingSetupBuilder setup)
    {
        throw new NotSupportedException(
            $"{GetType().Name} does not execute the production-capability conformance scenario."
        );
    }

    /// <summary>
    /// Opens an isolated session configured as <paramref name="options"/> asks. A leaf that declares a scenario needing
    /// one of the options overrides this; the default serves only the default options.
    /// </summary>
    protected virtual ValueTask<TransportConsumerConformanceSession> CreateSessionAsync(
        TransportConformanceSessionOptions options,
        CancellationToken cancellationToken
    )
    {
        return options == new TransportConformanceSessionOptions()
            ? CreateSessionAsync(cancellationToken)
            : throw new NotSupportedException($"{GetType().Name} does not configure {options}.");
    }

    /// <summary>
    /// Whether the provider keeps a message it gives up on in a dead-letter destination that
    /// <see cref="TakeDeadLetteredMessageIdsAsync"/> reads. Without one, giving up only stops redelivery.
    /// </summary>
    protected virtual bool HasDeadLetterDestination => false;

    /// <summary>
    /// Removes what the dead-letter destination of <paramref name="session"/> holds and returns the message id of each
    /// copy it removed, so one message dead-lettered twice shows twice.
    /// </summary>
    protected virtual ValueTask<IReadOnlyList<string>> TakeDeadLetteredMessageIdsAsync(
        TransportConsumerConformanceSession session,
        CancellationToken cancellationToken
    )
    {
        throw new NotSupportedException($"{GetType().Name} does not read a dead-letter destination.");
    }

    /// <summary>
    /// The largest body the provider accepts with the conformance headers, from the broker's documented message limit;
    /// a leaf that supports the body-size scenario overrides it.
    /// </summary>
    protected virtual int MaximumBodySize =>
        throw new NotSupportedException($"{GetType().Name} does not declare a maximum body size.");

    public virtual async Task should_match_production_runtime_capabilities()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessMessaging(ConfigureTransport);
        await using var serviceProvider = services.BuildServiceProvider();

        var actual = serviceProvider
            .GetServices<MessagingProviderCapabilities>()
            .Where(capabilities => capabilities.Role == MessagingProviderRole.Transport)
            .Should()
            .ContainSingle()
            .Subject;

        TransportConformanceManifest
            .Providers[ProviderName]
            .ExpectedRuntimeCapabilities.GetMismatchErrors(actual)
            .Should()
            .BeEmpty("the broker-backed manifest must describe the shipped provider descriptor exactly");
    }

    public virtual async Task should_round_trip_queue_message_body_and_headers()
    {
        RequireSupport(TransportConformanceScenario.QueueRoundTrip);
        RequireSupport(TransportConformanceScenario.HeaderRoundTrip);

        await using var session = await CreateSessionAsync(AbortToken);
        await session.StartAsync(cancellationToken: AbortToken);

        var expectedId = Guid.NewGuid().ToString("N");
        var expectedBody = "broker-observed-body"u8.ToArray();
        var message = _CreateMessage(session.Destination, expectedId, expectedBody);

        var result = await session.PublishAsync(message, AbortToken);
        result.Succeeded.Should().BeTrue();

        var delivery = await session.ReceiveAsync(TimeSpan.FromSeconds(10), AbortToken);
        delivery.Message.Body.ToArray().Should().Equal(expectedBody);
        delivery.Message.Id.Should().Be(expectedId);
        delivery.Message.Name.Should().Be(session.Destination);
        delivery.Message.Headers[MessagingHeaders.Intent].Should().Be(nameof(MessageLane.Queue));
        delivery.Message.Headers["x-headless-conformance"].Should().Be("round-trip");
        delivery.SettlementValue.Should().NotBeNull();

        await session.Consumer.CommitAsync(delivery.SettlementValue, AbortToken);
    }

    public virtual async Task should_dispatch_empty_message_body()
    {
        RequireSupport(TransportConformanceScenario.EmptyBodyDispatch);

        await using var session = await CreateSessionAsync(AbortToken);
        await session.StartAsync(cancellationToken: AbortToken);

        var message = _CreateMessage(session.Destination, Guid.NewGuid().ToString("N"), ReadOnlyMemory<byte>.Empty);
        var result = await session.PublishAsync(message, AbortToken);
        result.Succeeded.Should().BeTrue();

        var delivery = await session.ReceiveAsync(TimeSpan.FromSeconds(10), AbortToken);
        delivery.Message.Body.Length.Should().Be(0);
        await session.Consumer.CommitAsync(delivery.SettlementValue, AbortToken);
    }

    [Guarantee("MSG-SETTLE-03")]
    public virtual async Task should_commit_real_delivery_and_prevent_redelivery()
    {
        RequireSupport(TransportConformanceScenario.CommitSettlement);

        await using var session = await CreateSessionAsync(AbortToken);
        await session.StartAsync(cancellationToken: AbortToken);

        var message = _CreateMessage(
            session.Destination,
            Guid.NewGuid().ToString("N"),
            "commit-conformance"u8.ToArray()
        );
        var result = await session.PublishAsync(message, AbortToken);
        result.Succeeded.Should().BeTrue();

        var delivery = await session.ReceiveAsync(TimeSpan.FromSeconds(10), AbortToken);
        delivery.SettlementValue.Should().NotBeNull();
        await session.Consumer.CommitAsync(delivery.SettlementValue, AbortToken);

        await session.StopAsync(TimeSpan.FromSeconds(2));

        await using var replacement = await session.CreateReplacementAsync(AbortToken);
        await replacement.StartAsync(cancellationToken: AbortToken);

        var remainedSettled = await replacement.RemainsEmptyAsync(replacement.NoRedeliveryWindow, AbortToken);
        remainedSettled.Should().BeTrue("a replacement consumer must not receive a committed broker delivery");

        var replacementProbe = _CreateMessage(
            replacement.Destination,
            Guid.NewGuid().ToString("N"),
            "replacement-consumer-live"u8.ToArray()
        );
        var probeResult = await replacement.PublishAsync(replacementProbe, AbortToken);
        probeResult.Succeeded.Should().BeTrue();

        var probeDelivery = await replacement.ReceiveAsync(TimeSpan.FromSeconds(10), AbortToken);
        probeDelivery.Message.Id.Should().Be(replacementProbe.Id);
        await replacement.Consumer.CommitAsync(probeDelivery.SettlementValue, AbortToken);
    }

    [Guarantee("MSG-SETTLE-03")]
    public virtual async Task should_reject_real_delivery_and_observe_redelivery()
    {
        RequireSupport(TransportConformanceScenario.RejectRedelivery);

        await using var session = await CreateSessionAsync(AbortToken);
        await session.StartAsync(cancellationToken: AbortToken);

        var expectedId = Guid.NewGuid().ToString("N");
        var message = _CreateMessage(session.Destination, expectedId, "reject-conformance"u8.ToArray());
        var result = await session.PublishAsync(message, AbortToken);
        result.Succeeded.Should().BeTrue();

        var first = await session.ReceiveAsync(TimeSpan.FromSeconds(10), AbortToken);
        first.SettlementValue.Should().NotBeNull();
        await session.Consumer.RejectAsync(first.SettlementValue, AbortToken);

        var redelivery = await session.ReceiveAsync(TimeSpan.FromSeconds(10), AbortToken);
        redelivery.Message.Id.Should().Be(expectedId);
        redelivery.SettlementValue.Should().NotBeNull();
        ReferenceEquals(first.SettlementValue, redelivery.SettlementValue).Should().BeFalse();
        await session.Consumer.CommitAsync(redelivery.SettlementValue, AbortToken);
    }

    /// <summary>
    /// A settlement value that was already used must not settle anything else. Brokers that settle by a running
    /// delivery tag or offset can otherwise acknowledge a different in-flight delivery when the stale value is replayed.
    /// </summary>
    [Guarantee("MSG-SETTLE-04")]
    public virtual async Task should_not_settle_another_delivery_when_a_settled_value_is_used_again()
    {
        RequireSupport(TransportConformanceScenario.StaleSettlement);

        await using var session = await CreateSessionAsync(AbortToken);
        await session.StartAsync(cancellationToken: AbortToken);

        var first = _CreateMessage(session.Destination, Guid.NewGuid().ToString("N"), "stale-first"u8.ToArray());
        (await session.PublishAsync(first, AbortToken)).Succeeded.Should().BeTrue();
        var firstDelivery = await session.ReceiveAsync(TimeSpan.FromSeconds(10), AbortToken);
        await session.Consumer.CommitAsync(firstDelivery.SettlementValue, AbortToken);

        var second = _CreateMessage(session.Destination, Guid.NewGuid().ToString("N"), "stale-second"u8.ToArray());
        (await session.PublishAsync(second, AbortToken)).Succeeded.Should().BeTrue();
        var secondDelivery = await session.ReceiveAsync(TimeSpan.FromSeconds(10), AbortToken);
        secondDelivery.Message.Id.Should().Be(second.Id);

        try
        {
            await session.Consumer.CommitAsync(firstDelivery.SettlementValue, AbortToken);
        }
#pragma warning disable CA1031, ERP022 // A provider may refuse the stale value; the invariant under test is that nothing else settles.
        catch (Exception)
        {
            // Refusing a stale value is as valid as ignoring it.
        }
#pragma warning restore CA1031, ERP022

        await session.Consumer.RejectAsync(secondDelivery.SettlementValue, AbortToken);

        var redelivery = await session.ReceiveAsync(TimeSpan.FromSeconds(15), AbortToken);
        redelivery.Message.Id.Should().Be(second.Id, "the stale commit must not have settled the in-flight delivery");
        await session.Consumer.CommitAsync(redelivery.SettlementValue, AbortToken);

        (await session.RemainsEmptyAsync(session.NoRedeliveryWindow, AbortToken))
            .Should()
            .BeTrue("the stale commit must not replay the first delivery either");
    }

    /// <summary>
    /// A callback that throws is how a dispatch failure reaches the client. The delivery must stay available and the
    /// listener must keep consuming; a lost delivery or a dead receive loop shows only as silence.
    /// </summary>
    [Guarantee("MSG-SETTLE-05")]
    public virtual async Task should_keep_the_delivery_and_the_listener_when_the_callback_throws()
    {
        RequireSupport(TransportConformanceScenario.HandlerFailureRedelivery);

        await using var session = await CreateSessionAsync(AbortToken);
        var attempts = 0;
        await session.StartAsync(
            _ =>
                Interlocked.Increment(ref attempts) == 1
                    ? throw new InvalidOperationException("conformance callback failure")
                    : Task.CompletedTask,
            cancellationToken: AbortToken
        );

        var message = _CreateMessage(session.Destination, Guid.NewGuid().ToString("N"), "callback-fails"u8.ToArray());
        (await session.PublishAsync(message, AbortToken)).Succeeded.Should().BeTrue();

        var first = await session.ReceiveAsync(TimeSpan.FromSeconds(10), AbortToken);
        first.Message.Id.Should().Be(message.Id);

        // The failed delivery is never settled by the test: only the client may make it available again.
        var again = await session.ReceiveAsync(TimeSpan.FromSeconds(30), AbortToken);
        again.Message.Id.Should().Be(message.Id, "a delivery whose callback threw must not be lost");
        await session.Consumer.CommitAsync(again.SettlementValue, AbortToken);

        var probe = _CreateMessage(session.Destination, Guid.NewGuid().ToString("N"), "after-failure"u8.ToArray());
        (await session.PublishAsync(probe, AbortToken)).Succeeded.Should().BeTrue();
        var probeDelivery = await session.ReceiveAsync(TimeSpan.FromSeconds(10), AbortToken);
        probeDelivery.Message.Id.Should().Be(probe.Id, "the listener must survive a callback failure");
        await session.Consumer.CommitAsync(probeDelivery.SettlementValue, AbortToken);
    }

    public virtual async Task should_isolate_unique_destinations()
    {
        RequireSupport(TransportConformanceScenario.QueueRoundTrip);

        await using var firstSession = await CreateSessionAsync(AbortToken);
        await using var secondSession = await CreateSessionAsync(AbortToken);
        await firstSession.StartAsync(cancellationToken: AbortToken);
        await secondSession.StartAsync(cancellationToken: AbortToken);

        var message = _CreateMessage(
            firstSession.Destination,
            Guid.NewGuid().ToString("N"),
            "isolated-destination"u8.ToArray()
        );
        var result = await firstSession.PublishAsync(message, AbortToken);
        result.Succeeded.Should().BeTrue();

        var delivery = await firstSession.ReceiveAsync(TimeSpan.FromSeconds(10), AbortToken);
        await firstSession.Consumer.CommitAsync(delivery.SettlementValue, AbortToken);

        var secondStayedEmpty = await secondSession.RemainsEmptyAsync(TimeSpan.FromSeconds(1), AbortToken);
        secondStayedEmpty.Should().BeTrue("providers must isolate independently provisioned destinations");
    }

    [Guarantee("MSG-SHUT-03")]
    public virtual async Task should_shutdown_idle_consumer_within_bound()
    {
        RequireSupport(TransportConformanceScenario.BoundedGracefulShutdown);

        await using var session = await CreateSessionAsync(AbortToken);
        await session.StartAsync(cancellationToken: AbortToken);
        var stopwatch = Stopwatch.StartNew();

        await session.StopAsync(TimeSpan.FromSeconds(2));

        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(3));
    }

    [Guarantee("MSG-SHUT-03")]
    public virtual async Task should_bound_shutdown_while_handler_is_active()
    {
        RequireSupport(TransportConformanceScenario.BoundedGracefulShutdown);

        await using var session = await CreateSessionAsync(AbortToken);
        var handlerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseHandler = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await session.StartAsync(
            async _ =>
            {
                handlerStarted.TrySetResult();
                await releaseHandler.Task.ConfigureAwait(false);
            },
            cancellationToken: AbortToken
        );

        var message = _CreateMessage(session.Destination, Guid.NewGuid().ToString("N"), "active-shutdown"u8.ToArray());
        var result = await session.PublishAsync(message, AbortToken);
        result.Succeeded.Should().BeTrue();
        await handlerStarted.Task.WaitAsync(TimeSpan.FromSeconds(10), AbortToken);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            await session.StopAsync(TimeSpan.FromMilliseconds(500));
        }
        finally
        {
            releaseHandler.TrySetResult();
        }

        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2));
    }

    /// <summary>
    /// A custom headers builder that fails on a transient error, such as its lookup service timing out, must not cost the
    /// message: the broker hands it over again, and once the delivery budget is spent it gives up on it, keeping a
    /// dead-letter copy when it has a destination for one, instead of retrying forever.
    /// </summary>
    [Fact]
    [Guarantee("MSG-SETTLE-08")]
    public virtual async Task should_redeliver_then_give_up_when_the_header_builder_fails_transiently()
    {
        RequireSupport(TransportConformanceScenario.HeaderBuilderFailureRedelivery);

        const int deliveryLimit = 3;
        var recovering = Guid.NewGuid().ToString("N");
        var poisoned = Guid.NewGuid().ToString("N");
        var attempts = new ConcurrentDictionary<string, int>(StringComparer.Ordinal);

        await using var session = await CreateSessionAsync(
            new TransportConformanceSessionOptions
            {
                HeaderBuilder = messageId =>
                {
                    var attempt = attempts.AddOrUpdate(messageId ?? string.Empty, 1, static (_, count) => count + 1);
                    if (
                        string.Equals(messageId, poisoned, StringComparison.Ordinal)
                        || (string.Equals(messageId, recovering, StringComparison.Ordinal) && attempt == 1)
                    )
                    {
                        throw new TimeoutException("The conformance header lookup timed out.");
                    }
                },
                DeliveryLimit = deliveryLimit,
                DeadLettering = HasDeadLetterDestination,
            },
            AbortToken
        );
        await session.StartAsync(cancellationToken: AbortToken);

        (
            await session.PublishAsync(
                _CreateMessage(session.Destination, recovering, "recovers"u8.ToArray()),
                AbortToken
            )
        )
            .Succeeded.Should()
            .BeTrue();
        var delivery = await session.ReceiveAsync(TimeSpan.FromSeconds(30), AbortToken);
        delivery.Message.Id.Should().Be(recovering, "a transient builder failure must lead to redelivery, not loss");
        attempts[recovering].Should().Be(2);
        await session.Consumer.CommitAsync(delivery.SettlementValue, AbortToken);

        (
            await session.PublishAsync(
                _CreateMessage(session.Destination, poisoned, "keeps-failing"u8.ToArray()),
                AbortToken
            )
        )
            .Succeeded.Should()
            .BeTrue();
        var deadLettered = new List<string>();
        await _WaitUntilAsync(
            session,
            async token =>
            {
                if (!HasDeadLetterDestination)
                {
                    return attempts.GetValueOrDefault(poisoned) >= deliveryLimit;
                }

                deadLettered.AddRange(await TakeDeadLetteredMessageIdsAsync(session, token).ConfigureAwait(false));
                return deadLettered.Contains(poisoned, StringComparer.Ordinal);
            },
            TimeSpan.FromSeconds(60),
            () =>
                $"the broker never gave up on a message whose builder kept failing, after {attempts.GetValueOrDefault(poisoned)} attempts"
        );

        (await session.RemainsEmptyAsync(session.NoRedeliveryWindow, AbortToken))
            .Should()
            .BeTrue("a message whose builder never succeeded must not reach the consumer");
        attempts[poisoned]
            .Should()
            .BeInRange(2, deliveryLimit, "the broker must redeliver the message, and only within its budget");

        if (HasDeadLetterDestination)
        {
            deadLettered.AddRange(await TakeDeadLetteredMessageIdsAsync(session, AbortToken));
            deadLettered.Count(id => string.Equals(id, poisoned, StringComparison.Ordinal)).Should().Be(1);
        }
    }

    /// <summary>
    /// Dead-lettering a poisoned delivery settles it once: the original is not handed over again, and the dead-letter
    /// destination holds exactly one copy for broker tooling to inspect or move.
    /// </summary>
    [Fact]
    [Guarantee("MSG-SETTLE-07")]
    public virtual async Task should_keep_one_dead_letter_copy_and_settle_the_original_once()
    {
        RequireSupport(TransportConformanceScenario.DeadLetterSingleCopy);

        await using var session = await CreateSessionAsync(
            new TransportConformanceSessionOptions { DeadLettering = true },
            AbortToken
        );
        await session.StartAsync(cancellationToken: AbortToken);

        var message = _CreateMessage(session.Destination, Guid.NewGuid().ToString("N"), "poisoned"u8.ToArray());
        (await session.PublishAsync(message, AbortToken)).Succeeded.Should().BeTrue();
        var delivery = await session.ReceiveAsync(TimeSpan.FromSeconds(10), AbortToken);
        delivery.Message.Id.Should().Be(message.Id);

        await session.Consumer.DeadLetterAsync(
            delivery.SettlementValue,
            "ConformancePoison",
            "The conformance suite poisoned this delivery.",
            AbortToken
        );

        var deadLettered = new List<string>();
        await _WaitUntilAsync(
            session,
            async token =>
            {
                deadLettered.AddRange(await TakeDeadLetteredMessageIdsAsync(session, token).ConfigureAwait(false));
                return deadLettered.Contains(message.Id, StringComparer.Ordinal);
            },
            TimeSpan.FromSeconds(30),
            () => "the dead-lettered delivery never reached the dead-letter destination"
        );

        (await session.RemainsEmptyAsync(session.NoRedeliveryWindow, AbortToken))
            .Should()
            .BeTrue("a dead-lettered delivery is settled and must not be redelivered");
        deadLettered.AddRange(await TakeDeadLetteredMessageIdsAsync(session, AbortToken));
        deadLettered.Should().ContainSingle().Which.Should().Be(message.Id);
    }

    /// <summary>
    /// Every standard header the core writes on publish reaches the consumer unchanged. A provider that maps one onto a
    /// native property, or drops one, otherwise shows up downstream as a lost tenant, a broken trace, or a wrong type.
    /// </summary>
    [Fact]
    [Guarantee("MSG-ENV-01")]
    public virtual async Task should_round_trip_every_standard_header_unchanged()
    {
        RequireSupport(TransportConformanceScenario.StandardHeaderRoundTrip);

        await using var session = await CreateSessionAsync(AbortToken);
        await session.StartAsync(cancellationToken: AbortToken);

        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [MessagingHeaders.Type] = "ConformanceOrderPlaced",
            [MessagingHeaders.CorrelationId] = Guid.NewGuid().ToString("N"),
            [MessagingHeaders.SentTime] = new DateTimeOffset(2026, 10, 17, 18, 54, 3, 120, TimeSpan.Zero).ToString(
                "O",
                CultureInfo.InvariantCulture
            ),
            [MessagingHeaders.TenantId] = "tenant-42",
            [MessagingHeaders.TraceParent] = "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
        };
        var message = _CreateMessage(
            session.Destination,
            Guid.NewGuid().ToString("N"),
            "standard-headers"u8.ToArray(),
            expected
        );

        (await session.PublishAsync(message, AbortToken)).Succeeded.Should().BeTrue();
        var delivery = await session.ReceiveAsync(TimeSpan.FromSeconds(10), AbortToken);

        delivery.Message.Id.Should().Be(message.Id);
        foreach (var (name, value) in expected)
        {
            delivery
                .Message.Headers.Should()
                .ContainKey(name)
                .WhoseValue.Should()
                .Be(value, $"{name} must arrive unchanged");
        }

        await session.Consumer.CommitAsync(delivery.SettlementValue, AbortToken);
    }

    /// <summary>
    /// A consumer asked for concurrency two never runs a third delivery while two are in flight. Each delivery holds
    /// until the next one starts, so a client that ignores its cap overlaps every published message.
    /// </summary>
    [Fact]
    [Guarantee("MSG-CONSUME-09")]
    public virtual async Task should_never_hand_over_more_deliveries_at_once_than_the_requested_concurrency()
    {
        RequireSupport(TransportConformanceScenario.HandlerConcurrencyCap);

        const byte concurrency = 2;
        const int messageCount = 6;
        var started = new TaskCompletionSource[messageCount + 1];
        for (var i = 0; i < started.Length; i++)
        {
            started[i] = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        // The extra slot stands for "every message has started", so the last delivery does not wait for a next one.
        started[messageCount].TrySetResult();
        var startCount = 0;
        var inFlight = 0;
        var peak = 0;

        await using var session = await CreateSessionAsync(
            new TransportConformanceSessionOptions { Concurrency = concurrency },
            AbortToken
        );
        await session.StartAsync(
            async delivery =>
            {
                var current = Interlocked.Increment(ref inFlight);
                _RaiseTo(ref peak, current);
                var index = Math.Min(Interlocked.Increment(ref startCount), messageCount) - 1;
                started[index].TrySetResult();

                try
                {
                    // A client that hands deliveries over one at a time never starts the next one while this waits, so
                    // the wait gives up after a while rather than deadlock.
                    try
                    {
                        await started[index + 1]
                            .Task.WaitAsync(TimeSpan.FromSeconds(1), session.TimeProvider, AbortToken)
                            .ConfigureAwait(false);
                    }
                    catch (TimeoutException)
                    {
                        // Serial delivery stays within any cap.
                    }

                    await session.Consumer.CommitAsync(delivery.SettlementValue, CancellationToken.None);
                }
                finally
                {
                    Interlocked.Decrement(ref inFlight);
                }
            },
            cancellationToken: AbortToken
        );

        var published = new List<string>();
        for (var i = 0; i < messageCount; i++)
        {
            var message = _CreateMessage(session.Destination, Guid.NewGuid().ToString("N"), "concurrency"u8.ToArray());
            (await session.PublishAsync(message, AbortToken)).Succeeded.Should().BeTrue();
            published.Add(message.Id);
        }

        var received = new List<string>();
        for (var i = 0; i < messageCount; i++)
        {
            received.Add((await session.ReceiveAsync(TimeSpan.FromSeconds(30), AbortToken)).Message.Id);
        }

        received.Should().BeEquivalentTo(published);
        Volatile.Read(ref peak).Should().BeLessThanOrEqualTo(concurrency, "the client must honor its concurrency cap");
    }

    /// <summary>
    /// Messages that share a routing affinity key arrive in the order they were published, even when another key's
    /// messages are interleaved with them.
    /// </summary>
    [Fact]
    [Guarantee("MSG-LANE-03")]
    public virtual async Task should_deliver_the_messages_of_one_key_in_publish_order()
    {
        RequireSupport(TransportConformanceScenario.PerKeyOrdering);

        const int messagesPerKey = 5;
        string[] keys = ["order-a", "order-b"];
        var published = keys.ToDictionary(key => key, _ => new List<string>(), StringComparer.Ordinal);

        await using var session = await CreateSessionAsync(
            new TransportConformanceSessionOptions { RoutingAffinity = true },
            AbortToken
        );
        await session.StartAsync(cancellationToken: AbortToken);

        for (var i = 0; i < messagesPerKey; i++)
        {
            foreach (var key in keys)
            {
                var message = _CreateMessage(
                    session.Destination,
                    Guid.NewGuid().ToString("N"),
                    "ordered"u8.ToArray(),
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        [MessagingHeaders.RoutingAffinityKey] = key,
                    }
                );
                (await session.PublishAsync(message, AbortToken)).Succeeded.Should().BeTrue();
                published[key].Add(message.Id);
            }
        }

        var received = keys.ToDictionary(key => key, _ => new List<string>(), StringComparer.Ordinal);
        for (var i = 0; i < messagesPerKey * keys.Length; i++)
        {
            var delivery = await session.ReceiveAsync(TimeSpan.FromSeconds(30), AbortToken);
            received[delivery.Message.RoutingAffinityKey!].Add(delivery.Message.Id);
            await session.Consumer.CommitAsync(delivery.SettlementValue, AbortToken);
        }

        foreach (var key in keys)
        {
            received[key].Should().Equal(published[key], $"the messages of {key} must arrive in publish order");
        }
    }

    /// <summary>
    /// A body at the broker's documented limit arrives intact, and one byte more fails at publish instead of being
    /// accepted and lost.
    /// </summary>
    [Fact]
    [Guarantee("MSG-ENV-02")]
    public virtual async Task should_round_trip_a_body_at_the_size_limit_and_refuse_one_byte_more()
    {
        RequireSupport(TransportConformanceScenario.BodySizeLimit);

        var limit = MaximumBodySize;
        await using var session = await CreateSessionAsync(AbortToken);
        await session.StartAsync(cancellationToken: AbortToken);

        var atLimit = _CreateMessage(session.Destination, Guid.NewGuid().ToString("N"), _PatternBody(limit));
        var result = await session.PublishAsync(atLimit, AbortToken);
        result.Succeeded.Should().BeTrue($"a {limit}-byte body is within the limit: {result.Exception}");
        var delivery = await session.ReceiveAsync(TimeSpan.FromSeconds(30), AbortToken);
        delivery.Message.Id.Should().Be(atLimit.Id);
        delivery.Message.Body.Span.SequenceEqual(atLimit.Body.Span).Should().BeTrue("the body must arrive intact");
        await session.Consumer.CommitAsync(delivery.SettlementValue, AbortToken);

        var overLimit = _CreateMessage(session.Destination, Guid.NewGuid().ToString("N"), _PatternBody(limit + 1));
        bool refused;
        try
        {
            refused = !(await session.PublishAsync(overLimit, AbortToken)).Succeeded;
        }
#pragma warning disable CA1031 // A provider may report the refusal by throwing or by a failed result; both are refusals.
        catch (Exception exception) when (exception is not OperationCanceledException)
#pragma warning restore CA1031
        {
            refused = true;
        }

        refused.Should().BeTrue("a body one byte over the limit must fail at publish");

        // A publish reported as failed must also have left nothing behind: an oversized message the broker kept anyway
        // would reach the consumer, or break it, instead of the next message.
        var next = _CreateMessage(session.Destination, Guid.NewGuid().ToString("N"), "after-refusal"u8.ToArray());
        await session.PublishUntilSucceededAsync(next, TimeSpan.FromSeconds(30), AbortToken);
        var nextDelivery = await session.ReceiveAsync(TimeSpan.FromSeconds(30), AbortToken);
        nextDelivery.Message.Id.Should().Be(next.Id, "the refused message must not be delivered");
        await session.Consumer.CommitAsync(nextDelivery.SettlementValue, AbortToken);
        (await session.RemainsEmptyAsync(session.NoRedeliveryWindow, AbortToken))
            .Should()
            .BeTrue("a refused publish must not be delivered");
    }

    /// <summary>
    /// A message left unconsumed past its time-to-live is expired by the broker and never delivered, while a fresh one
    /// published afterwards is.
    /// </summary>
    [Fact]
    [Guarantee("MSG-ENV-03")]
    public virtual async Task should_not_deliver_a_message_that_expired_before_it_was_consumed()
    {
        RequireSupport(TransportConformanceScenario.MessageExpiry);

        var timeToLive = TimeSpan.FromSeconds(1);
        await using var session = await CreateSessionAsync(
            new TransportConformanceSessionOptions { TimeToLive = timeToLive },
            AbortToken
        );

        var expiring = _CreateMessage(session.Destination, Guid.NewGuid().ToString("N"), "expires"u8.ToArray());
        (await session.PublishAsync(expiring, AbortToken)).Succeeded.Should().BeTrue();
        await session.TimeProvider.Delay(timeToLive * 3, AbortToken);

        await session.StartAsync(cancellationToken: AbortToken);
        var fresh = _CreateMessage(session.Destination, Guid.NewGuid().ToString("N"), "fresh"u8.ToArray());
        (await session.PublishAsync(fresh, AbortToken)).Succeeded.Should().BeTrue();

        var delivery = await session.ReceiveAsync(TimeSpan.FromSeconds(10), AbortToken);
        delivery.Message.Id.Should().Be(fresh.Id, "the expired message must not be delivered");
        await session.Consumer.CommitAsync(delivery.SettlementValue, AbortToken);
        (await session.RemainsEmptyAsync(session.NoRedeliveryWindow, AbortToken)).Should().BeTrue();
    }

    /// <summary>Polls <paramref name="condition"/> on the session clock until it holds, or fails with <paramref name="because"/>.</summary>
    private async Task _WaitUntilAsync(
        TransportConsumerConformanceSession session,
        Func<CancellationToken, ValueTask<bool>> condition,
        TimeSpan timeout,
        Func<string> because
    )
    {
        var startedAt = session.TimeProvider.GetTimestamp();
        while (!await condition(AbortToken))
        {
            if (session.TimeProvider.GetElapsedTime(startedAt) > timeout)
            {
                throw new TimeoutException($"Waited {timeout}: {because()}.");
            }

            await session.TimeProvider.Delay(TimeSpan.FromMilliseconds(100), AbortToken);
        }
    }

    private static void _RaiseTo(ref int peak, int value)
    {
        var observed = Volatile.Read(ref peak);
        while (value > observed)
        {
            var previous = Interlocked.CompareExchange(ref peak, value, observed);
            if (previous == observed)
            {
                return;
            }

            observed = previous;
        }
    }

    /// <summary>
    /// A body whose bytes cycle through ASCII letters and digits, so a truncated or shifted copy cannot pass for it. It
    /// stays ASCII because a provider whose broker carries text bodies, such as SQS, sends the body as UTF-8.
    /// </summary>
    private static byte[] _PatternBody(int length)
    {
        const string alphabet = "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";
        var body = new byte[length];
        for (var i = 0; i < body.Length; i++)
        {
            body[i] = (byte)alphabet[i % alphabet.Length];
        }

        return body;
    }

    private static TransportMessage _CreateMessage(
        string destination,
        string messageId,
        ReadOnlyMemory<byte> body,
        IReadOnlyDictionary<string, string>? extraHeaders = null
    )
    {
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [MessagingHeaders.MessageId] = messageId,
            [MessagingHeaders.MessageName] = destination,
            [MessagingHeaders.Intent] = nameof(MessageLane.Queue),
            ["x-headless-conformance"] = "round-trip",
        };

        foreach (var (name, value) in extraHeaders ?? new Dictionary<string, string>(StringComparer.Ordinal))
        {
            headers[name] = value;
        }

        return new TransportMessage(headers, body);
    }
}
