// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging;
using Headless.Messaging.Transport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MessagingHeaders = Headless.Messaging.Headers;

namespace Tests.Simulation;

/// <summary>What happened at the simulated broker, recorded in <see cref="SimulatedBroker.Events"/>.</summary>
[PublicAPI]
public enum BrokerEventKind
{
    /// <summary>The broker accepted a publish and routed it to every subscribed destination.</summary>
    Published,

    /// <summary>A publish reached the broker while its connection was down and failed.</summary>
    PublishRefused,

    /// <summary>A consumer client received a delivery.</summary>
    Delivered,

    /// <summary>A live delivery was committed.</summary>
    Committed,

    /// <summary>A live delivery was rejected and goes back to its destination.</summary>
    Rejected,

    /// <summary>A live delivery was moved to the dead-letter destination.</summary>
    DeadLettered,

    /// <summary>A settlement named no live delivery of the client, such as a value from before a reconnect, and was ignored.</summary>
    SettlementRefused,

    /// <summary>A settlement of a live delivery failed by script; the delivery stays unsettled.</summary>
    SettlementFailed,

    /// <summary>A delivery went back to its destination to be delivered again.</summary>
    Requeued,

    /// <summary>A consumer client's receive failed, and the client reported the broker unreachable.</summary>
    ReceiveFailed,

    /// <summary>A consumer client's receive reached the broker again after a failure, and the client reported it.</summary>
    ReceiveRecovered,

    /// <summary>The broker's connection went down.</summary>
    ConnectionLost,

    /// <summary>The broker's connection came back.</summary>
    ConnectionRestored,

    /// <summary>A consumer client's channel closed under it.</summary>
    ChannelClosed,

    /// <summary>A consumer client finished closing.</summary>
    ClientClosed,
}

/// <summary>One entry of the simulated broker's record.</summary>
/// <param name="Kind">What happened.</param>
/// <param name="Subscription">The subscription of the client involved, or <see langword="null"/> for a broker-wide event.</param>
/// <param name="MessageId">The message involved, or <see langword="null"/>.</param>
/// <param name="DeliveryCount">How many times the message has been delivered, this delivery included; zero when none.</param>
/// <param name="At">The simulated time it happened.</param>
[PublicAPI]
public sealed record BrokerEvent(
    BrokerEventKind Kind,
    string? Subscription,
    string? MessageId,
    int DeliveryCount,
    DateTimeOffset At
);

/// <summary>A broker operation the simulation failed on purpose.</summary>
[PublicAPI]
public sealed class SimulatedBrokerException(string message) : Exception(message);

/// <summary>
/// An in-process broker that runs under the real messaging pipeline and fails on cue: its connection drops and comes
/// back, a client's receive or settlement fails, a channel closes, a client's close is slow or hangs. Time inside it is
/// the <see cref="SimulationClock"/>, so every retry and redelivery delay moves only when the test advances the clock.
/// </summary>
/// <remarks>
/// <para>
/// Bus messages reach every subscribed group, and a queue message reaches one; within a group, clients compete for
/// deliveries. A competing group outlives its clients, as a durable queue does, so a replacement client receives what
/// was left unsettled. An every-instance group goes with its last client.
/// </para>
/// <para>
/// Each delivery gets its own settlement value. Losing the connection or a channel returns every unsettled delivery to
/// its group and turns its value stale: settling with it later is ignored and recorded as
/// <see cref="BrokerEventKind.SettlementRefused"/>, so it can never settle the delivery that replaced it.
/// </para>
/// <para>
/// It also carries request/reply: each reply listener gets an address of its own, and a reply to an address nobody
/// listens on any more is dropped. Replies do not follow <see cref="Disconnect"/>; no fault is scripted for them yet.
/// </para>
/// <para>
/// Register it on a host with <c>setup.UseSimulatedBroker(broker)</c>, or drive it directly through
/// <see cref="CreateTransport"/> and <see cref="CreateConsumerClient"/>.
/// </para>
/// </remarks>
[PublicAPI]
public sealed class SimulatedBroker
{
    /// <summary>The transport name the simulated broker registers under.</summary>
    public const string ProviderName = "Simulator";

    private readonly Dictionary<(MessageLane Lane, string MessageName), List<string>> _routes = [];
    private readonly Dictionary<(MessageLane Lane, string GroupId), SimulatedGroup> _groups = [];
    private readonly Dictionary<string, int> _nextQueueGroup = new(StringComparer.Ordinal);
    private readonly List<BrokerEvent> _events = [];
    private readonly HashSet<SimulatedConsumerClient> _listening = [];
    private readonly List<SimulatedTimedWork> _timedWork = [];
    private readonly Dictionary<string, SimulatedReplyListener> _replyListeners = new(StringComparer.Ordinal);
    private TaskCompletionSource _changed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private TaskCompletionSource _activity = new(TaskCreationOptions.RunContinuationsAsynchronously);
#pragma warning disable IDE0032 // Read and written under the gate; an auto property would drop the lock.
    private long _generation;
    private bool _connected = true;
#pragma warning restore IDE0032

    /// <summary>Creates a broker on <paramref name="clock"/> that takes its faults from <paramref name="faults"/>.</summary>
    /// <param name="clock">The simulated time its retries and redelivery delays run on.</param>
    /// <param name="faults">The fault plan; a new empty one when omitted.</param>
    public SimulatedBroker(SimulationClock clock, FaultScript? faults = null)
    {
        Clock = Argument.IsNotNull(clock);
        Faults = faults ?? new FaultScript();
        Faults.Subscribe(Signal);
    }

    /// <summary>Gets the simulated time the broker runs on.</summary>
    public SimulationClock Clock { get; }

    /// <summary>Gets the fault plan the broker and its clients follow.</summary>
    public FaultScript Faults { get; }

    /// <summary>
    /// Gets how long a client waits after a failed receive before it tries again, in simulated time. Each retry that
    /// fails is reported to the host again.
    /// </summary>
    public TimeSpan RetryInterval { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>Gets the address every simulated transport and client reports.</summary>
    public BrokerAddress Address { get; } = new("simulator", "simulated-broker");

    /// <summary>Gets whether the broker's connection is up.</summary>
    public bool IsConnected
    {
        get
        {
            lock (Gate)
            {
                return _connected;
            }
        }
    }

    /// <summary>Gets a snapshot of everything that happened at the broker, oldest first.</summary>
    public IReadOnlyList<BrokerEvent> Events
    {
        get
        {
            lock (Gate)
            {
                return [.. _events];
            }
        }
    }

    // One gate guards the broker and every client's delivery state, so a reconnect requeues and invalidates every
    // unsettled delivery in one step that no settlement can interleave with.
    internal Lock Gate { get; } = new();

    /// <summary>Waits for the first event, oldest first, that matches <paramref name="predicate"/>.</summary>
    public async Task<BrokerEvent> WaitForEventAsync(
        Func<BrokerEvent, bool> predicate,
        CancellationToken cancellationToken = default
    )
    {
        var events = await WaitForEventsAsync(predicate, 1, cancellationToken).ConfigureAwait(false);
        return events[0];
    }

    /// <summary>Waits until <paramref name="count"/> events match <paramref name="predicate"/> and returns them, oldest first.</summary>
    public async Task<IReadOnlyList<BrokerEvent>> WaitForEventsAsync(
        Func<BrokerEvent, bool> predicate,
        int count,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(predicate);
        Argument.IsPositive(count);

        var matched = new List<BrokerEvent>(count);
        var scanned = 0;

        while (true)
        {
            Task changed;
            lock (Gate)
            {
                for (; scanned < _events.Count; scanned++)
                {
                    if (predicate(_events[scanned]))
                    {
                        matched.Add(_events[scanned]);
                        if (matched.Count == count)
                        {
                            return matched;
                        }
                    }
                }

                changed = _changed.Task;
            }

            await changed.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Lists the subscriptions whose groups receive <paramref name="messageName"/> on <paramref name="lane"/>.</summary>
    public IReadOnlyList<string> SubscriptionsFor(MessageLane lane, string messageName)
    {
        lock (Gate)
        {
            return _routes.TryGetValue((lane, messageName), out var groupIds)
                ?
                [
                    .. groupIds
                        .Select(groupId => _groups[(lane, groupId)].Subscription)
                        .Distinct(StringComparer.Ordinal),
                ]
                : [];
        }
    }

    /// <summary>
    /// Waits until nothing at the broker can happen before simulated time moves: every listening client has seen the
    /// broker's latest change and waits for work or for its retry time, no message callback is running, and no
    /// redelivery or close the clock already released is still pending. Only then can moving the clock not overtake
    /// work that was already due.
    /// </summary>
    /// <remarks>
    /// It covers the broker and its consumer clients only. A host's own pipeline, such as its dispatcher and store, runs
    /// outside it, so a host test advances the clock itself.
    /// </remarks>
    /// <param name="cancellationToken">Abandons the wait.</param>
    public async Task WhenQuiescentAsync(CancellationToken cancellationToken = default)
    {
        while (true)
        {
            Task activity;
            lock (Gate)
            {
                if (_IsQuiescentLocked())
                {
                    return;
                }

                activity = _activity.Task;
            }

            await activity.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Gets whether a reply listener is open at <paramref name="address"/>.</summary>
    public bool HasReplyListener(string address)
    {
        lock (Gate)
        {
            return _replyListeners.ContainsKey(address);
        }
    }

    /// <summary>
    /// Drops the broker's connection. Publishes fail as unreachable, clients report each failed receive and retry on
    /// <see cref="RetryInterval"/>, new clients fail to connect, and every unsettled delivery goes back to its group
    /// with its settlement value made stale.
    /// </summary>
    public void Disconnect()
    {
        lock (Gate)
        {
            if (!_connected)
            {
                return;
            }

            _connected = false;
            foreach (var client in _groups.Values.SelectMany(static group => group.Clients))
            {
                client.RequeueInFlightLocked();
            }

            RecordLocked(BrokerEventKind.ConnectionLost, subscription: null, messageId: null, deliveryCount: 0);
        }
    }

    /// <summary>Brings the broker's connection back; clients reach it again on their next retry.</summary>
    public void Restore()
    {
        lock (Gate)
        {
            if (_connected)
            {
                return;
            }

            _connected = true;
            RecordLocked(BrokerEventKind.ConnectionRestored, subscription: null, messageId: null, deliveryCount: 0);
        }
    }

    /// <summary>
    /// Closes the channel of every client of <paramref name="subscription"/>: each stops receiving, reports
    /// <see cref="MqLogType.ConsumerShutdown"/>, and its unsettled deliveries go back to the group with stale values.
    /// </summary>
    public void CloseChannels(string subscription)
    {
        Argument.IsNotNullOrWhiteSpace(subscription);

        List<SimulatedConsumerClient> closed;
        lock (Gate)
        {
            closed =
            [
                .. _groups
                    .Values.Where(group => string.Equals(group.Subscription, subscription, StringComparison.Ordinal))
                    .SelectMany(static group => group.Clients)
                    .Where(static client => client.CloseChannelLocked()),
            ];
        }

        // The log callback runs core code, so it is invoked outside the gate.
        foreach (var client in closed)
        {
            client.ReportChannelClosed();
        }
    }

    /// <summary>Creates a producer that publishes to the broker on <paramref name="lane"/>.</summary>
    public ITransport CreateTransport(MessageLane lane)
    {
        return lane switch
        {
            MessageLane.Bus => new SimulatedBusTransport(this),
            MessageLane.Queue => new SimulatedQueueTransport(this),
            _ => throw new ArgumentOutOfRangeException(
                nameof(lane),
                lane,
                "The simulated broker has a bus and a queue lane."
            ),
        };
    }

    /// <summary>Creates a consumer client for <paramref name="request"/>, as the transport's client factory does.</summary>
    /// <exception cref="BrokerConnectionException">The broker's connection is down.</exception>
    public SimulatedConsumerClient CreateConsumerClient(ConsumerClientRequest request)
    {
        Argument.IsNotNull(request);
        EnsureConnected();

        return new SimulatedConsumerClient(this, request);
    }

    internal void EnsureConnected()
    {
        lock (Gate)
        {
            if (!_connected)
            {
                throw new BrokerConnectionException(
                    new SimulatedBrokerException("The simulated broker's connection is down.")
                );
            }
        }
    }

    internal OperateResult Publish(MessageLane lane, TransportMessage message)
    {
        lock (Gate)
        {
            var messageId = MessageIdOf(message);
            if (!_connected)
            {
                RecordLocked(BrokerEventKind.PublishRefused, subscription: null, messageId, deliveryCount: 0);
                return OperateResult.BrokerUnreachable(
                    new SimulatedBrokerException("The simulated broker's connection is down; the publish was not sent.")
                );
            }

            if (_routes.TryGetValue((lane, message.Name), out var groupIds) && groupIds.Count > 0)
            {
                if (lane is MessageLane.Queue)
                {
                    // A queue message has one owner: the subscribed groups take turns.
                    var next = _nextQueueGroup.GetValueOrDefault(message.Name) % groupIds.Count;
                    _nextQueueGroup[message.Name] = next + 1;
                    _groups[(lane, groupIds[next])].Pending.Enqueue(new SimulatedPending(Copy(message), 1));
                }
                else
                {
                    foreach (var groupId in groupIds)
                    {
                        _groups[(lane, groupId)].Pending.Enqueue(new SimulatedPending(Copy(message), 1));
                    }
                }
            }

            // A publish nobody subscribed to is accepted and dropped, as by a real broker.
            RecordLocked(BrokerEventKind.Published, subscription: null, messageId, deliveryCount: 0);
            return OperateResult.Success;
        }
    }

    internal SimulatedGroup Subscribe(SimulatedConsumerClient client, string groupId, IEnumerable<string> messageNames)
    {
        lock (Gate)
        {
            if (!_connected)
            {
                throw new BrokerConnectionException(
                    new SimulatedBrokerException("The simulated broker's connection is down; the subscription failed.")
                );
            }

            var request = client.Request;
            var key = (request.Lane, groupId);
            if (!_groups.TryGetValue(key, out var group))
            {
                group = new SimulatedGroup(
                    request.Lane,
                    groupId,
                    request.SubscriptionName,
                    request.Kind is ConsumerSubscriptionKind.EveryInstance
                );
                _groups[key] = group;
            }

            foreach (var messageName in messageNames)
            {
                if (!_routes.TryGetValue((request.Lane, messageName), out var groupIds))
                {
                    groupIds = [];
                    _routes[(request.Lane, messageName)] = groupIds;
                }

                if (!groupIds.Contains(groupId, StringComparer.Ordinal))
                {
                    groupIds.Add(groupId);
                }
            }

            if (!group.Clients.Contains(client))
            {
                group.Clients.Add(client);
            }

            SignalLocked();
            return group;
        }
    }

    /// <summary>Takes <paramref name="client"/> out of <paramref name="group"/>; an every-instance group goes with its last client.</summary>
    internal void RemoveClientLocked(SimulatedConsumerClient client, SimulatedGroup group)
    {
        group.Clients.Remove(client);
        if (!group.EveryInstance || group.Clients.Count > 0)
        {
            return;
        }

        _groups.Remove((group.Lane, group.GroupId));
        foreach (var (key, groupIds) in _routes.ToArray())
        {
            if (key.Lane == group.Lane && groupIds.Remove(group.GroupId) && groupIds.Count == 0)
            {
                _routes.Remove(key);
            }
        }
    }

    internal bool IsConnectedLocked => _connected;

    internal Task ChangedLocked => _changed.Task;

    /// <summary>Counts every broker change; a client that saw the current value has nothing new to react to.</summary>
    internal long GenerationLocked => _generation;

    internal void StartListeningLocked(SimulatedConsumerClient client)
    {
        _listening.Add(client);
        PulseActivityLocked();
    }

    internal void StopListeningLocked(SimulatedConsumerClient client)
    {
        _listening.Remove(client);
        PulseActivityLocked();
    }

    /// <summary>Registers work the clock releases at <paramref name="at"/>, so quiescence waits for it once it is due.</summary>
    internal SimulatedTimedWork AddTimedWorkLocked(DateTimeOffset at)
    {
        var work = new SimulatedTimedWork(at);
        _timedWork.Add(work);
        return work;
    }

    internal void CompleteTimedWorkLocked(SimulatedTimedWork work)
    {
        _timedWork.Remove(work);
        PulseActivityLocked();
    }

    /// <summary>Wakes a quiescence waiter to check again, without counting as a broker change.</summary>
    internal void PulseActivityLocked()
    {
        var activity = _activity;
        _activity = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        activity.TrySetResult();
    }

    internal void RegisterReplyListener(SimulatedReplyListener listener)
    {
        lock (Gate)
        {
            _replyListeners.Add(listener.Address, listener);
        }
    }

    internal void UnregisterReplyListener(SimulatedReplyListener listener)
    {
        lock (Gate)
        {
            if (
                _replyListeners.TryGetValue(listener.Address, out var registered)
                && ReferenceEquals(registered, listener)
            )
            {
                _replyListeners.Remove(listener.Address);
            }
        }
    }

    /// <summary>Hands a copy of <paramref name="reply"/> to the listener at <paramref name="address"/>, or drops it when none listens.</summary>
    internal void SendReply(string address, TransportMessage reply)
    {
        SimulatedReplyListener? listener;
        lock (Gate)
        {
            _replyListeners.TryGetValue(address, out listener);
        }

        // The listener queues the reply for its own pump, so the caller's handling never runs on the responder's stack.
        listener?.Deliver(Copy(reply));
    }

    internal void RecordLocked(BrokerEventKind kind, string? subscription, string? messageId, int deliveryCount)
    {
        _events.Add(new BrokerEvent(kind, subscription, messageId, deliveryCount, Clock.GetUtcNow()));
        SignalLocked();
    }

    internal void Record(BrokerEventKind kind, string? subscription, string? messageId, int deliveryCount)
    {
        lock (Gate)
        {
            RecordLocked(kind, subscription, messageId, deliveryCount);
        }
    }

    /// <summary>Wakes every client waiting for work and every event waiter.</summary>
    internal void Signal()
    {
        lock (Gate)
        {
            SignalLocked();
        }
    }

    internal void SignalLocked()
    {
        // Continuations run asynchronously, so completing the old signal never runs a waiter under the gate.
        _generation++;
        var changed = _changed;
        _changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        changed.TrySetResult();
        PulseActivityLocked();
    }

    private bool _IsQuiescentLocked()
    {
        var now = Clock.GetUtcNow();

        if (_timedWork.Exists(work => work.At <= now))
        {
            return false;
        }

        foreach (var client in _listening)
        {
            if (!client.IsSettledLocked(_generation))
            {
                return false;
            }
        }

        return true;
    }

    internal static string? MessageIdOf(TransportMessage message)
    {
        return message.Headers.TryGetValue(MessagingHeaders.MessageId, out var id) ? id : null;
    }

    /// <summary>Copies the headers, so no receiver's changes reach another receiver or the broker's own copy.</summary>
    internal static TransportMessage Copy(TransportMessage message)
    {
        return new TransportMessage(
            new Dictionary<string, string?>(message.Headers, StringComparer.Ordinal),
            message.Body
        );
    }
}

/// <summary>A message waiting in a group for a client to take it.</summary>
internal sealed record SimulatedPending(TransportMessage Message, int DeliveryCount);

/// <summary>Broker work waiting on the simulation clock, such as a delayed redelivery or a slow close.</summary>
internal sealed class SimulatedTimedWork(DateTimeOffset at)
{
    public DateTimeOffset At { get; } = at;
}

/// <summary>A destination at the simulated broker: one consumer group on one lane.</summary>
internal sealed class SimulatedGroup(MessageLane lane, string groupId, string subscription, bool everyInstance)
{
    public MessageLane Lane { get; } = lane;

    public string GroupId { get; } = groupId;

    public string Subscription { get; } = subscription;

    public bool EveryInstance { get; } = everyInstance;

    // Guarded by the broker gate, as is everything below.
    public Queue<SimulatedPending> Pending { get; } = new();

    public List<SimulatedConsumerClient> Clients { get; } = [];
}

/// <summary>Publishes to the simulated broker's bus lane.</summary>
internal sealed class SimulatedBusTransport(SimulatedBroker broker) : IBusTransport
{
    private int _disposed;

    public BrokerAddress BrokerAddress => broker.Address;

    public Task<OperateResult> SendAsync(TransportMessage message, CancellationToken cancellationToken = default)
    {
        return Volatile.Read(ref _disposed) != 0
            ? Task.FromResult(OperateResult.Failed(new ObjectDisposedException(nameof(SimulatedBusTransport))))
            : SimulatedTransportCore.SendAsync(broker, MessageLane.Bus, message, cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        Volatile.Write(ref _disposed, 1);
        return ValueTask.CompletedTask;
    }
}

/// <summary>Publishes to the simulated broker's queue lane.</summary>
internal sealed class SimulatedQueueTransport(SimulatedBroker broker) : IQueueTransport
{
    private int _disposed;

    public BrokerAddress BrokerAddress => broker.Address;

    public Task<OperateResult> SendAsync(TransportMessage message, CancellationToken cancellationToken = default)
    {
        return Volatile.Read(ref _disposed) != 0
            ? Task.FromResult(OperateResult.Failed(new ObjectDisposedException(nameof(SimulatedQueueTransport))))
            : SimulatedTransportCore.SendAsync(broker, MessageLane.Queue, message, cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        Volatile.Write(ref _disposed, 1);
        return ValueTask.CompletedTask;
    }
}

internal static class SimulatedTransportCore
{
    public static Task<OperateResult> SendAsync(
        SimulatedBroker broker,
        MessageLane lane,
        TransportMessage message,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        MessagingRoutingAffinityMapping.RejectUnsupported(message, SimulatedBroker.ProviderName);
        return Task.FromResult(broker.Publish(lane, message));
    }
}

/// <summary>Creates the simulated broker's consumer clients for the messaging core.</summary>
internal sealed class SimulatedConsumerClientFactory(SimulatedBroker broker) : IConsumerClientFactory
{
    public Task<IConsumerClient> CreateAsync(
        ConsumerClientRequest request,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IConsumerClient>(broker.CreateConsumerClient(request));
    }
}

/// <summary>Registers the <see cref="SimulatedBroker"/> as a host's message transport.</summary>
[PublicAPI]
public static class SetupSimulatedBroker
{
    extension(MessagingSetupBuilder setup)
    {
        /// <summary>
        /// Registers <paramref name="broker"/> as the transport, for both lanes, through the same extension point a
        /// broker package uses, so the host runs the real pipeline against it.
        /// </summary>
        /// <param name="broker">The broker the host publishes to and consumes from; hosts that share one share messages.</param>
        /// <returns>The same builder for chaining.</returns>
        public MessagingSetupBuilder UseSimulatedBroker(SimulatedBroker broker)
        {
            Argument.IsNotNull(broker);
            setup.RegisterExtension(new SimulatedBrokerOptionsExtension(broker));
            return setup;
        }
    }

    private sealed class SimulatedBrokerOptionsExtension(SimulatedBroker broker) : IMessagesOptionsExtension
    {
        public void AddServices(IServiceCollection services)
        {
            services.AddSingleton(new MessageQueueMarkerService(SimulatedBroker.ProviderName));
            services.AddMessagingProviderCapabilities(
                MessagingProviderCapabilities.Transport(
                    SimulatedBroker.ProviderName,
                    [MessageLane.Bus, MessageLane.Queue],
                    supportsIndependentLaneTopology: true,
                    supportsEveryInstance: true,
                    supportsRequestReply: true
                )
            );
            services.AddSingleton(broker);
            services.AddSingleton<IConsumerClientFactory>(new SimulatedConsumerClientFactory(broker));
            services.AddSingleton(_ => new SimulatedBusTransport(broker));
            services.AddSingleton<IBusTransport>(sp => sp.GetRequiredService<SimulatedBusTransport>());
            services.AddSingleton<ITransport>(sp => sp.GetRequiredService<SimulatedBusTransport>());
            services.AddSingleton(_ => new SimulatedQueueTransport(broker));
            services.AddSingleton<IQueueTransport>(sp => sp.GetRequiredService<SimulatedQueueTransport>());
            services.AddSingleton<IReplyTransport>(sp => new SimulatedReplyTransport(
                broker,
                sp.GetRequiredService<ILogger<SimulatedReplyTransport>>()
            ));
        }
    }
}
