// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Channels;
using Headless.Messaging;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;
using Tests.Capabilities;
using MessagingHeaders = Headless.Messaging.Headers;

namespace Tests;

/// <summary>A broker-delivered message and the provider's opaque settlement value.</summary>
[PublicAPI]
public sealed record TransportConformanceDelivery(TransportMessage Message, object? SettlementValue);

/// <summary>Owns one isolated producer/consumer pair used by broker-backed conformance tests.</summary>
[PublicAPI]
public sealed class TransportConsumerConformanceSession(
    string destination,
    ITransport producer,
    IConsumerClient consumer,
    TimeSpan noRedeliveryWindow,
    Func<ValueTask>? disposeProviderResources = null,
    TimeSpan? listeningTimeout = null,
    Func<CancellationToken, ValueTask<TransportConsumerConformanceSession>>? createReplacementSession = null
) : IAsyncDisposable
{
    private readonly Channel<TransportConformanceDelivery> _deliveries =
        Channel.CreateUnbounded<TransportConformanceDelivery>(
            new UnboundedChannelOptions { SingleWriter = false, SingleReader = false }
        );
    private readonly ConcurrentQueue<LogMessageEventArgs> _logs = new();

    private readonly TimeSpan _listeningTimeout = listeningTimeout ?? TimeSpan.FromSeconds(2);
    private CancellationTokenSource? _listeningCts;
    private Task? _listeningTask;
    private int _stopped;
    private int _disposed;

    public string Destination { get; } = destination;

    public TimeSpan NoRedeliveryWindow { get; } = noRedeliveryWindow;

    public IConsumerClient Consumer { get; } = consumer;

    public async Task StartAsync(
        Func<TransportConformanceDelivery, Task>? onDelivery = null,
        CancellationToken cancellationToken = default
    )
    {
        if (_listeningTask is not null)
        {
            throw new InvalidOperationException("The conformance session has already started.");
        }

        Consumer.AttachCallbacks(
            onMessage: async (message, settlementValue) =>
            {
                var delivery = new TransportConformanceDelivery(message, settlementValue);
                await _deliveries.Writer.WriteAsync(delivery, CancellationToken.None).ConfigureAwait(false);

                if (onDelivery is not null)
                {
                    await onDelivery(delivery).ConfigureAwait(false);
                }
            },
            onLog: _logs.Enqueue
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
        using var cts = timeout.ToCancellationTokenSource(cancellationToken);
        try
        {
            return await _deliveries.Reader.ReadAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
            when (cts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            var diagnostics = string.Join(Environment.NewLine, _logs.Select(log => $"{log.LogType}: {log.Reason}"));
            throw new TimeoutException(
                $"No broker delivery arrived within {timeout}. Consumer diagnostics:{Environment.NewLine}{diagnostics}"
            );
        }
    }

    public async Task<bool> RemainsEmptyAsync(TimeSpan window, CancellationToken cancellationToken = default)
    {
        using var cts = window.ToCancellationTokenSource(cancellationToken);

        try
        {
            _ = await _deliveries.Reader.ReadAsync(cts.Token).ConfigureAwait(false);
            return false;
        }
        catch (OperationCanceledException)
            when (cts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            return true;
        }
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
}

/// <summary>Shared broker-observed transport, delivery, and settlement invariants.</summary>
[PublicAPI]
public abstract class TransportConsumerConformanceTestsBase : TestBase
{
    protected abstract string ProviderName { get; }

    protected abstract ValueTask<TransportConsumerConformanceSession> CreateSessionAsync(
        CancellationToken cancellationToken
    );

    public virtual async Task should_round_trip_queue_message_body_and_headers()
    {
        _RequireSupport(TransportConformanceScenario.QueueRoundTrip);
        _RequireSupport(TransportConformanceScenario.HeaderRoundTrip);

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
        delivery.Message.Headers["x-headless-conformance"].Should().Be("round-trip");
        delivery.SettlementValue.Should().NotBeNull();

        await session.Consumer.CommitAsync(delivery.SettlementValue, AbortToken);
    }

    public virtual async Task should_dispatch_empty_message_body()
    {
        _RequireSupport(TransportConformanceScenario.EmptyBodyDispatch);

        await using var session = await CreateSessionAsync(AbortToken);
        await session.StartAsync(cancellationToken: AbortToken);

        var message = _CreateMessage(session.Destination, Guid.NewGuid().ToString("N"), ReadOnlyMemory<byte>.Empty);
        var result = await session.PublishAsync(message, AbortToken);
        result.Succeeded.Should().BeTrue();

        var delivery = await session.ReceiveAsync(TimeSpan.FromSeconds(10), AbortToken);
        delivery.Message.Body.Length.Should().Be(0);
        await session.Consumer.CommitAsync(delivery.SettlementValue, AbortToken);
    }

    public virtual async Task should_commit_real_delivery_and_prevent_redelivery()
    {
        _RequireSupport(TransportConformanceScenario.CommitSettlement);

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

    public virtual async Task should_reject_real_delivery_and_observe_redelivery()
    {
        _RequireSupport(TransportConformanceScenario.RejectRedelivery);

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

    public virtual async Task should_isolate_unique_destinations()
    {
        _RequireSupport(TransportConformanceScenario.QueueRoundTrip);

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

    public virtual async Task should_shutdown_idle_consumer_within_bound()
    {
        _RequireSupport(TransportConformanceScenario.BoundedGracefulShutdown);

        await using var session = await CreateSessionAsync(AbortToken);
        await session.StartAsync(cancellationToken: AbortToken);
        var stopwatch = Stopwatch.StartNew();

        await session.StopAsync(TimeSpan.FromSeconds(2));

        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(3));
    }

    public virtual async Task should_bound_shutdown_while_handler_is_active()
    {
        _RequireSupport(TransportConformanceScenario.BoundedGracefulShutdown);

        await using var session = await CreateSessionAsync(AbortToken);
        var handlerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseHandler = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await session.StartAsync(
            async _ =>
            {
                handlerStarted.TrySetResult();
                await releaseHandler.Task.ConfigureAwait(false);
            },
            AbortToken
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

    protected void RequireSupport(TransportConformanceScenario scenario)
    {
        _RequireSupport(scenario);
    }

    private void _RequireSupport(TransportConformanceScenario scenario)
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

    private static TransportMessage _CreateMessage(string destination, string messageId, ReadOnlyMemory<byte> body)
    {
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [MessagingHeaders.MessageId] = messageId,
            [MessagingHeaders.MessageName] = destination,
            ["x-headless-conformance"] = "round-trip",
        };

        return new TransportMessage(headers, body);
    }
}
