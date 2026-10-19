// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging;
using Headless.Messaging.Transport;

namespace Tests.Simulation;

/// <summary>
/// The settlement value of one delivery by the simulated broker. It settles that delivery only while the delivery is
/// live: once the delivery is settled, or the connection or channel it came on is lost, the value is stale.
/// </summary>
[PublicAPI]
public sealed class SimulatedDelivery
{
    internal SimulatedDelivery(SimulatedPending pending, TransportMessage message, SimulatedConsumerClient client)
    {
        Pending = pending;
        Message = message;
        Client = client;
    }

    /// <summary>Gets the message as the client handed it to its callback.</summary>
    public TransportMessage Message { get; }

    /// <summary>Gets how many times the message has been delivered, this delivery included.</summary>
    public int DeliveryCount => Pending.DeliveryCount;

    internal SimulatedPending Pending { get; }

    internal SimulatedConsumerClient Client { get; }
}

/// <summary>
/// A consumer client of the <see cref="SimulatedBroker"/>. It behaves as a broker client that retries a lost broker on
/// its own: a failed receive is reported to the host's broker reachability and retried after the broker's
/// <see cref="SimulatedBroker.RetryInterval"/> of simulated time, and the first receive that reaches the broker again
/// reports the recovery. Only this client's own failures end in its recovery report, so another client's healthy receives
/// never clear its failure run.
/// </summary>
[PublicAPI]
public sealed class SimulatedConsumerClient : IConsumerClient
{
    private readonly SimulatedBroker _broker;
    private readonly BrokerReachabilityReporter _reachability = new();
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly SemaphoreSlim? _slots;

    // The same tracker the broker clients use, so a closing client waits for its running callbacks the way they do.
    private readonly InFlightHandlerTracker _inFlightHandlers = new();

    // Guarded by the broker gate.
    private readonly HashSet<SimulatedDelivery> _inFlight = [];
    private SimulatedGroup? _group;
    private bool _paused;
    private bool _channelClosed;
    private bool _closed;
    private bool _listening;

    // The broker generation the receive loop last saw before it went to wait, or -1 while it is working; with no
    // callback running, a loop parked at the current generation has nothing to do until the broker or the clock moves.
    private long _parkedGeneration = -1;
    private int _callbacksInFlight;

    private Task? _closeTask;
    private Func<CancellationToken, Task>? _onReestablished;

    internal SimulatedConsumerClient(SimulatedBroker broker, ConsumerClientRequest request)
    {
        _broker = broker;
        Request = request;
        GroupId =
            request.Kind is ConsumerSubscriptionKind.EveryInstance
                ? $"{request.SubscriptionName}.{request.InstanceId:N}"
                : request.SubscriptionName;
        _slots = request.Concurrency > 0 ? new SemaphoreSlim(request.Concurrency) : null;
    }

    /// <summary>Gets the subscription the client was opened for.</summary>
    public string Subscription => Request.SubscriptionName;

    /// <summary>Gets the broker group the client competes in; an every-instance client has a group of its own.</summary>
    public string GroupId { get; }

    /// <summary>Gets how many deliveries the client holds unsettled.</summary>
    public int InFlightCount
    {
        get
        {
            lock (_broker.Gate)
            {
                return _inFlight.Count;
            }
        }
    }

    /// <summary>Gets whether the client is paused, so it takes no delivery.</summary>
    public bool IsPaused
    {
        get
        {
            lock (_broker.Gate)
            {
                return _paused;
            }
        }
    }

    public BrokerAddress BrokerAddress => _broker.Address;

    internal bool IsOpenLocked => !_closed && !_channelClosed;

    public Func<TransportMessage, object?, Task>? OnMessageCallback { get; private set; }

    public Action<LogMessageEventArgs>? OnLogCallback { get; private set; }

    internal ConsumerClientRequest Request { get; }

    public void AttachCallbacks(Func<TransportMessage, object?, Task>? onMessage, Action<LogMessageEventArgs>? onLog)
    {
        OnMessageCallback = onMessage;
        OnLogCallback = onLog;
    }

    public void AttachReestablishedCallback(Func<CancellationToken, Task>? onReestablished)
    {
        _onReestablished = onReestablished;
    }

    public ValueTask<ICollection<string>> FetchMessageNamesAsync(
        IEnumerable<string> messageNames,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(messageNames);
        cancellationToken.ThrowIfCancellationRequested();
        _broker.EnsureConnected();

        return ValueTask.FromResult<ICollection<string>>([.. messageNames]);
    }

    public ValueTask SubscribeAsync(IEnumerable<string> messageNames, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNull(messageNames);
        cancellationToken.ThrowIfCancellationRequested();

        var group = _broker.Subscribe(this, GroupId, messageNames);
        bool listening;
        lock (_broker.Gate)
        {
            _group = group;
            listening = _listening;
        }

        if (listening)
        {
            _ready.TrySetResult();
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>Completes once the client is subscribed and its receive loop runs, so a publish after it is not missed.</summary>
    public ValueTask WaitUntilReadyAsync(CancellationToken cancellationToken = default)
    {
        return new ValueTask(_ready.Task.WaitAsync(cancellationToken));
    }

    /// <summary>
    /// Receives until the client closes, its channel closes, or <paramref name="cancellationToken"/> is canceled. It
    /// never polls on a timer: it wakes when the broker changes, and backs off a failed receive on the simulation clock.
    /// </summary>
    /// <param name="timeout">Unused; the loop wakes on broker changes instead of polling.</param>
    /// <param name="cancellationToken">Stops the loop.</param>
    public async ValueTask ListeningAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        bool subscribed;
        lock (_broker.Gate)
        {
            _listening = true;
            subscribed = _group is not null;
            _broker.StartListeningLocked(this);
        }

        // Ready only once the loop counts as listening: a broker waiting for quiescence must see it before a test can
        // publish and start waiting.
        if (subscribed)
        {
            _ready.TrySetResult();
        }

        try
        {
            await _ListenAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            lock (_broker.Gate)
            {
                _parkedGeneration = -1;
                _broker.StopListeningLocked(this);
            }
        }
    }

    private async Task _ListenAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset? retryAt = null;
        var lostConnection = false;
        var slotHeld = false;

        while (!cancellationToken.IsCancellationRequested)
        {
            if (_slots is not null && !slotHeld)
            {
                await _slots.WaitAsync(cancellationToken).ConfigureAwait(false);
                slotHeld = true;
            }

            var step = _Poll(retryAt);

            switch (step.Kind)
            {
                case PollKind.Stopped:
                    _ReleaseSlot(ref slotHeld);
                    return;
                case PollKind.BackingOff:
                    await _BackOffAsync(step.Changed, retryAt!.Value, cancellationToken).ConfigureAwait(false);
                    continue;
                case PollKind.Unreachable:
                case PollKind.ReceiveFailed:
                    lostConnection |= step.Kind is PollKind.Unreachable;
                    _reachability.ReportFailure(OnLogCallback, step.Reason!);
                    _broker.Record(BrokerEventKind.ReceiveFailed, Subscription, messageId: null, deliveryCount: 0);

                    // Measured from the failure, not from when the loop waits, so a test that advances the clock as soon
                    // as it sees the failure cannot leave the retry waiting for another advance.
                    retryAt = step.Now + _broker.RetryInterval;
                    continue;
            }

            // The receive reached the broker.
            retryAt = null;
            if (_reachability.HasPendingFailure)
            {
                _reachability.ReportSuccess(OnLogCallback);
                _broker.Record(BrokerEventKind.ReceiveRecovered, Subscription, messageId: null, deliveryCount: 0);

                if (lostConnection && _onReestablished is { } onReestablished)
                {
                    await onReestablished(cancellationToken).ConfigureAwait(false);
                }

                lostConnection = false;
            }

            if (step.Delivery is null)
            {
                await _WaitAsync(step.Changed, other: null, cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (_slots is null)
            {
                var inline = _InvokeAsync(step.Delivery);
                _inFlightHandlers.Track(inline);
                await inline.ConfigureAwait(false);
                continue;
            }

            // The slot passes to the delivery and is released when its callback returns.
            slotHeld = false;
            var delivery = step.Delivery;
            _inFlightHandlers.Track(
                Task.Run(
                    async () =>
                    {
                        try
                        {
                            await _InvokeAsync(delivery).ConfigureAwait(false);
                        }
                        finally
                        {
                            _slots.Release();
                        }
                    },
                    CancellationToken.None
                )
            );
        }

        _ReleaseSlot(ref slotHeld);
        cancellationToken.ThrowIfCancellationRequested();
    }

    // Each settlement honours its token as a broker client's network call does: a canceled settlement never reaches the
    // broker, so a caller that settles with a token shutdown cancels leaves the delivery unsettled.
    public ValueTask CommitAsync(object? sender, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _Settle(sender, BrokerEventKind.Committed, redeliveryDelay: null);
        return ValueTask.CompletedTask;
    }

    public ValueTask RejectAsync(object? sender, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _Settle(sender, BrokerEventKind.Rejected, TimeSpan.Zero);
        return ValueTask.CompletedTask;
    }

    // The broker holds the delivery for the delay itself, on the simulation clock, so the core's own hold is not needed.
    public ValueTask RejectAsync(object? sender, TimeSpan redeliveryDelay, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _Settle(sender, BrokerEventKind.Rejected, redeliveryDelay);
        return ValueTask.CompletedTask;
    }

    public ValueTask DeadLetterAsync(
        object? sender,
        string reason,
        string? description,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        _Settle(sender, BrokerEventKind.DeadLettered, redeliveryDelay: null);
        return ValueTask.CompletedTask;
    }

    public async ValueTask PauseAsync(CancellationToken cancellationToken = default)
    {
        await _broker.Faults.TakePauseHold().WaitAsync(cancellationToken).ConfigureAwait(false);
        _SetPaused(paused: true);
    }

    public ValueTask ResumeAsync(CancellationToken cancellationToken = default)
    {
        _SetPaused(paused: false);
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Closes the client once its running callbacks finish, waiting for them at most <paramref name="timeout"/> of
    /// simulated time, as a broker client drains its handlers before it closes the channel their settlement needs.
    /// </summary>
    public ValueTask ShutdownAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        return new ValueTask(_CloseAsync(timeout));
    }

    /// <summary>Closes the client without waiting for its running callbacks, unless a shutdown already began.</summary>
    public ValueTask DisposeAsync()
    {
        return new ValueTask(_CloseAsync(TimeSpan.Zero));
    }

    /// <summary>Returns every unsettled delivery to the group and makes its settlement value stale.</summary>
    internal void RequeueInFlightLocked()
    {
        if (_group is null)
        {
            return;
        }

        foreach (var delivery in _inFlight)
        {
            _Requeue(_group, delivery.Pending);
        }

        _inFlight.Clear();
    }

    /// <summary>Closes the channel under the client; false when it was already closed.</summary>
    internal bool CloseChannelLocked()
    {
        if (_closed || _channelClosed)
        {
            return false;
        }

        _channelClosed = true;
        RequeueInFlightLocked();
        _broker.RecordLocked(BrokerEventKind.ChannelClosed, Subscription, messageId: null, deliveryCount: 0);
        return true;
    }

    /// <summary>
    /// Closes this client's channel alone: it stops receiving, reports <see cref="MqLogType.ConsumerShutdown"/>, and its
    /// unsettled deliveries go back to the group with stale values.
    /// </summary>
    public void CloseChannel()
    {
        bool closed;
        lock (_broker.Gate)
        {
            closed = CloseChannelLocked();
        }

        if (closed)
        {
            ReportChannelClosed();
        }
    }

    /// <summary>
    /// Reports <see cref="MqLogType.ConsumerRegistered"/>, as a broker client does when the broker confirms its
    /// consumer, for example after it recovers a channel on its own.
    /// </summary>
    public void ReportRegistered()
    {
        OnLogCallback?.Invoke(
            new LogMessageEventArgs
            {
                LogType = MqLogType.ConsumerRegistered,
                Reason = $"The simulated consumer of '{Subscription}' is registered.",
            }
        );
    }

    internal void ReportChannelClosed()
    {
        OnLogCallback?.Invoke(
            new LogMessageEventArgs
            {
                LogType = MqLogType.ConsumerShutdown,
                Reason = $"The simulated channel of '{Subscription}' closed.",
            }
        );
    }

    private PollStep _Poll(DateTimeOffset? retryAt)
    {
        lock (_broker.Gate)
        {
            var step = _PollLocked(retryAt);

            // Read after the poll, whose own records move the generation: the loop waits next only on these two steps.
            _parkedGeneration =
                step.Kind is PollKind.BackingOff || (step.Kind is PollKind.Reached && step.Delivery is null)
                    ? _broker.GenerationLocked
                    : -1;

            if (step.Delivery is not null)
            {
                _callbacksInFlight++;
            }

            // Parking can complete quiescence, which no broker change announces.
            _broker.PulseActivityLocked();
            return step;
        }
    }

    private PollStep _PollLocked(DateTimeOffset? retryAt)
    {
        var now = _broker.Clock.GetUtcNow();
        var changed = _broker.ChangedLocked;

        if (_closed || _channelClosed)
        {
            return new PollStep(PollKind.Stopped, now, changed);
        }

        if (retryAt > now)
        {
            return new PollStep(PollKind.BackingOff, now, changed);
        }

        if (!_broker.IsConnectedLocked)
        {
            return new PollStep(PollKind.Unreachable, now, changed, Reason: "The simulated broker is unreachable.");
        }

        if (_broker.Faults.TryTakeReceiveFault(Subscription, out var reason))
        {
            return new PollStep(PollKind.ReceiveFailed, now, changed, Reason: reason);
        }

        if (_paused || _group is null || !_group.Pending.TryDequeue(out var pending))
        {
            return new PollStep(PollKind.Reached, now, changed);
        }

        var delivery = new SimulatedDelivery(pending, SimulatedBroker.Copy(pending.Message), this);
        _inFlight.Add(delivery);
        _broker.RecordLocked(
            BrokerEventKind.Delivered,
            Subscription,
            SimulatedBroker.MessageIdOf(pending.Message),
            pending.DeliveryCount
        );

        return new PollStep(PollKind.Reached, now, changed, Delivery: delivery);
    }

    /// <summary>Whether the receive loop has nothing to do until the broker changes past <paramref name="generation"/>.</summary>
    internal bool IsSettledLocked(long generation)
    {
        return _parkedGeneration == generation && _callbacksInFlight == 0;
    }

    private async Task _InvokeAsync(SimulatedDelivery delivery)
    {
        try
        {
            if (!_TryBuildHeaders(delivery))
            {
                return;
            }

            await (OnMessageCallback?.Invoke(delivery.Message, delivery) ?? Task.CompletedTask).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // A callback failure is the client's to absorb: the delivery goes back, as an unacknowledged one does at a broker.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            OnLogCallback?.Invoke(
                new LogMessageEventArgs
                {
                    LogType = MqLogType.ExceptionReceived,
                    Reason = $"The message callback failed: {ex}",
                }
            );

            lock (_broker.Gate)
            {
                if (_group is not null && _inFlight.Remove(delivery))
                {
                    _Requeue(_group, delivery.Pending);
                }
            }
        }
        finally
        {
            lock (_broker.Gate)
            {
                _callbacksInFlight--;
                _broker.PulseActivityLocked();
            }
        }
    }

    /// <summary>Runs the broker's custom headers builder; a builder that throws settles the delivery as malformed.</summary>
    private bool _TryBuildHeaders(SimulatedDelivery delivery)
    {
        if (_broker.CustomHeadersBuilder is not { } builder)
        {
            return true;
        }

        try
        {
            builder(delivery.Message.Headers);
            return true;
        }
#pragma warning disable CA1031 // A builder is user code that may throw anything; the providers it models settle every failure as malformed.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            OnLogCallback?.Invoke(
                new LogMessageEventArgs
                {
                    LogType = MqLogType.ExceptionReceived,
                    Reason = $"The custom headers builder failed: {ex.GetType().Name}.",
                }
            );
            _Settle(delivery, BrokerEventKind.Committed, redeliveryDelay: null);
            return false;
        }
    }

    private void _Settle(object? sender, BrokerEventKind outcome, TimeSpan? redeliveryDelay)
    {
        lock (_broker.Gate)
        {
            var delivery = sender as SimulatedDelivery;
            if (delivery is null || _group is null || !_inFlight.Contains(delivery))
            {
                _broker.RecordLocked(
                    BrokerEventKind.SettlementRefused,
                    Subscription,
                    delivery is null ? null : SimulatedBroker.MessageIdOf(delivery.Pending.Message),
                    delivery?.DeliveryCount ?? 0
                );
                return;
            }

            var messageId = SimulatedBroker.MessageIdOf(delivery.Pending.Message);
            if (_broker.Faults.TryTakeSettlementFault())
            {
                _broker.RecordLocked(BrokerEventKind.SettlementFailed, Subscription, messageId, delivery.DeliveryCount);
                throw new SimulatedBrokerException($"The simulated broker failed to settle message '{messageId}'.");
            }

            _inFlight.Remove(delivery);
            _broker.RecordLocked(outcome, Subscription, messageId, delivery.DeliveryCount);

            if (redeliveryDelay is not { } delay)
            {
                return;
            }

            if (delay <= TimeSpan.Zero)
            {
                _Requeue(_group, delivery.Pending);
                return;
            }

            var at = _broker.Clock.GetUtcNow() + delay;
            _ = _RequeueAtAsync(_group, delivery.Pending, at, _broker.AddTimedWorkLocked(at));
        }
    }

    private async Task _RequeueAtAsync(
        SimulatedGroup group,
        SimulatedPending pending,
        DateTimeOffset at,
        SimulatedTimedWork work
    )
    {
        await _broker.Clock.WhenReachedAsync(at).ConfigureAwait(false);

        lock (_broker.Gate)
        {
            _Requeue(group, pending);
            _broker.CompleteTimedWorkLocked(work);
        }
    }

    private void _Requeue(SimulatedGroup group, SimulatedPending pending)
    {
        if (_broker.MaxDeliveries is { } limit && pending.DeliveryCount >= limit)
        {
            // The broker's own limit: the delivery goes to the dead-letter destination instead of coming back.
            _broker.RecordLocked(
                BrokerEventKind.DeadLettered,
                group.Subscription,
                SimulatedBroker.MessageIdOf(pending.Message),
                pending.DeliveryCount
            );
            return;
        }

        var redelivery = pending with { DeliveryCount = pending.DeliveryCount + 1 };
        group.Pending.Enqueue(redelivery);
        _broker.RecordLocked(
            BrokerEventKind.Requeued,
            group.Subscription,
            SimulatedBroker.MessageIdOf(pending.Message),
            pending.DeliveryCount
        );
    }

    private void _SetPaused(bool paused)
    {
        lock (_broker.Gate)
        {
            _paused = paused;
            _broker.RecordLocked(
                paused ? BrokerEventKind.Paused : BrokerEventKind.Resumed,
                Subscription,
                messageId: null,
                deliveryCount: 0
            );
        }
    }

    private Task _CloseAsync(TimeSpan drainTimeout)
    {
        lock (_broker.Gate)
        {
            return _closeTask ??= _CloseCoreAsync(drainTimeout);
        }
    }

    private async Task _CloseCoreAsync(TimeSpan drainTimeout)
    {
        bool subscribed;
        lock (_broker.Gate)
        {
            _closed = true;
            subscribed = _group is not null;
            _broker.SignalLocked();
        }

        _ready.TrySetCanceled();

        // A callback still running past the bound settles against a closed client and is refused; its delivery went
        // back to the group below, which is what a broker does with a delivery its closed channel never settled. The
        // bound is broker work due at its deadline, so a clock that moves on quiescence cannot overtake the close once
        // the bound has passed.
        SimulatedTimedWork? drain = null;
        if (drainTimeout > TimeSpan.Zero && _inFlightHandlers.Count > 0)
        {
            lock (_broker.Gate)
            {
                drain = _broker.AddTimedWorkLocked(_broker.Clock.GetUtcNow() + drainTimeout);
            }
        }

        try
        {
            await _inFlightHandlers.DrainAsync(drainTimeout, _broker.Clock).ConfigureAwait(false);
        }
#pragma warning disable CA1031, ERP022 // The drain is best effort: a callback that timed out or faulted must not stop the close.
        catch (Exception)
        {
            // The close goes on: the unsettled deliveries are returned below.
        }
#pragma warning restore CA1031, ERP022

        // A close fault holds only a client that opened a subscription: the core's short-lived topology clients have no
        // connection to tear down, so a fault armed before startup does not stall the bootstrap.
        SimulatedTimedWork? slowClose = null;
        if (subscribed)
        {
            var (kind, duration, release) = _broker.Faults.TakeCloseFault();
            if (kind is CloseFaultKind.Slow)
            {
                DateTimeOffset closesAt;
                lock (_broker.Gate)
                {
                    closesAt = _broker.Clock.GetUtcNow() + duration;
                    slowClose = _broker.AddTimedWorkLocked(closesAt);
                }

                await _broker.Clock.WhenReachedAsync(closesAt).ConfigureAwait(false);
            }
            else if (kind is CloseFaultKind.Hung)
            {
                await release.ConfigureAwait(false);
            }
        }

        lock (_broker.Gate)
        {
            RequeueInFlightLocked();
            if (_group is not null)
            {
                _broker.RemoveClientLocked(this, _group);
            }

            _broker.RecordLocked(BrokerEventKind.ClientClosed, Subscription, messageId: null, deliveryCount: 0);

            if (slowClose is not null)
            {
                _broker.CompleteTimedWorkLocked(slowClose);
            }

            if (drain is not null)
            {
                _broker.CompleteTimedWorkLocked(drain);
            }
        }
    }

    private void _ReleaseSlot(ref bool slotHeld)
    {
        if (slotHeld)
        {
            _slots!.Release();
            slotHeld = false;
        }
    }

    /// <summary>Waits for the retry time or for a broker change, such as a close, whichever comes first.</summary>
    private async Task _BackOffAsync(Task changed, DateTimeOffset retryAt, CancellationToken cancellationToken)
    {
        // The deadline is withdrawn when the change wins, so an abandoned retry never lingers as a pending clock wait.
        using var withdraw = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var retry = _broker.Clock.WhenReachedAsync(retryAt, withdraw.Token);

        try
        {
            await _WaitAsync(changed, retry, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await withdraw.CancelAsync().ConfigureAwait(false);
        }
    }

    private static async Task _WaitAsync(Task changed, Task? other, CancellationToken cancellationToken)
    {
        try
        {
            await (other is null ? changed : Task.WhenAny(changed, other))
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The loop checks the token next and stops.
        }
    }

    private enum PollKind
    {
        Stopped,
        BackingOff,
        Unreachable,
        ReceiveFailed,
        Reached,
    }

    private readonly record struct PollStep(
        PollKind Kind,
        DateTimeOffset Now,
        Task Changed,
        string? Reason = null,
        SimulatedDelivery? Delivery = null
    );
}
