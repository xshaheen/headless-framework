// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Threading.Channels;
using Headless.Checks;
using Headless.Messaging.Internal;
using Headless.Messaging.Transport;
using Headless.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;

namespace Headless.Messaging.Nats;

internal sealed class NatsConsumerClient(
    string name,
    byte groupConcurrent,
    IOptions<NatsMessagingOptions> options,
    IServiceProvider serviceProvider,
    Func<string, ConsumerConfig, CancellationToken, Task<INatsJSConsumer>>? consumerFactory = null,
    MessageLane lane = MessageLane.Bus,
    TimeProvider? timeProvider = null,
    Func<NatsConnection, Task>? connect = null,
    Func<NatsConnection, ValueTask>? disposeConnection = null,
    ConsumerSubscriptionKind kind = ConsumerSubscriptionKind.Competing
) : IConsumerClient
{
    private readonly Lock _receiveLock = new();
    private readonly NatsMessagingOptions _natsOptions = Argument.IsNotNull(options.Value);
    private readonly NatsStreamProvisioner _streamProvisioner = new(options);
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    private readonly SemaphoreSlim? _semaphore = groupConcurrent > 0 ? new SemaphoreSlim(groupConcurrent) : null;

    // Tracks in-flight fire-and-forget handler tasks on the concurrent (groupConcurrent > 0) path so
    // DisposeAsync can drain them before disposing the semaphore and connection.
    private readonly ConcurrentDictionary<Task, byte> _inFlightHandlers = new();

    // Bounded drain budget on shutdown. Aligned with the default AckWait (30s): a handler still
    // running past this would have its message redelivered by JetStream anyway (at-least-once).
    private static readonly TimeSpan _ShutdownDrainTimeout = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan _DefaultAckWait = TimeSpan.FromSeconds(30);

    // The first server release that parses "+TERM <reason>"; earlier ones match the exact "+TERM" bytes only.
    private static readonly Version _TerminateReasonServerVersion = new(2, 10, 4);

    // How long one pull request lives on the server. The consume loop re-pulls before it expires, so this bounds only
    // how long a pull the server lost stays unnoticed; 30 s is the nats.go and NATS.Net default.
    private static readonly TimeSpan _PullExpires = TimeSpan.FromSeconds(30);

    // The server sends a heartbeat this often on an idle pull, and NATS.Net reports a timeout after two missed ones, so a
    // dead pull or connection shows up within ten seconds instead of the 30 s NATS.Net derives from the expiry.
    private static readonly TimeSpan _PullIdleHeartbeat = TimeSpan.FromSeconds(5);

#pragma warning disable CA2213 // Disposal is deferred until the tokenless SDK connection attempt settles.
    private NatsConnection? _connection;
#pragma warning restore CA2213
    private Task? _connectTask;
    private NatsJSContext? _jsContext;
    private ReceiveTokenState _receiveTokenState = new();
    private IEnumerable<string>? _subscribedMessageNames;
    private int _disposed;

    // Set by ConnectAsync for an every-instance client, which listens on core subscriptions instead of JetStream.
    private NatsEveryInstanceListener? _everyInstance;

    public Func<TransportMessage, object?, Task>? OnMessageCallback { get; set; }

    public Action<LogMessageEventArgs>? OnLogCallback { get; set; }

    public void AttachCallbacks(Func<TransportMessage, object?, Task>? onMessage, Action<LogMessageEventArgs>? onLog)
    {
        OnMessageCallback = onMessage;
        OnLogCallback = onLog;
    }

    public void AttachReestablishedCallback(Func<CancellationToken, Task>? onReestablished)
    {
        OnReestablished = onReestablished;
    }

    /// <summary>Whether shutdown has started; a listener stops reporting to the consumer from then on.</summary>
    internal bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    /// <summary>Completes once the client receives; the every-instance listener completes it for its subscriptions.</summary>
    internal TaskCompletionSource Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>The pause gate every receive loop waits on.</summary>
    internal ConsumerPauseGate PauseGate { get; } = new();

    /// <summary>The core's re-established callback, reported when the subscription may have missed messages.</summary>
    internal Func<CancellationToken, Task>? OnReestablished { get; private set; }

    public BrokerAddress BrokerAddress =>
        new(
            "nats",
            _natsOptions.ConnectionFactory is null
                ? BrokerAddressDisplay.FormatMany(_natsOptions.Servers)
                : serviceProvider.GetRequiredService<INatsConnectionPool>().ServersAddress
        );

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        // MaxReconnectRetry = 0 does not disable reconnect: NATS.Net only enforces the limit when it is positive, so
        // 0 (like the -1 default) retries without limit and the SDK re-sends every SUB on reconnect. A competing
        // client's JetStream consumer is durable, so its consume loop resumes on the reconnected socket, and a loop
        // that keeps failing trips MaxConsecutiveConsumeFailures and surfaces for a supervised rebuild. An
        // every-instance client learns about the gap through ConnectionOpened below. The circuit breaker is
        // per-message and never observes connection-level faults.
        // A connection the application supplied decides the servers and credentials; the consumer copies its options
        // onto a socket of its own (see NatsMessagingOptions.UseConnection).
        var baseOpts = _natsOptions.ConnectionFactory is null
            ? _natsOptions.BuildNatsOpts()
            : serviceProvider.GetRequiredService<INatsConnectionPool>().ConnectionOpts;
        var opts = baseOpts with { MaxReconnectRetry = 0 };

        var connection = new NatsConnection(opts);
        _connection = connection;

        if (kind is ConsumerSubscriptionKind.EveryInstance)
        {
            _everyInstance = new NatsEveryInstanceListener(this, name, _timeProvider, connection);
        }

        var connectTask = connect?.Invoke(connection) ?? connection.ConnectAsync().AsTask();
        _connectTask = connectTask;

        await connectTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        _jsContext = new NatsJSContext(connection);
    }

    public async ValueTask<ICollection<string>> FetchMessageNamesAsync(
        IEnumerable<string> messageNames,
        CancellationToken cancellationToken = default
    )
    {
        // Materialize once: the source is consumed by provisioning and the return value, so a lazy
        // input would otherwise be enumerated twice.
        var names = messageNames.AsIReadOnlyList();

        await _streamProvisioner
            .EnsureAsync(_jsContext!, lane, names, ResolveShardedMessageNames(names), cancellationToken)
            .ConfigureAwait(false);

        return [.. names];
    }

    internal static string BuildDurableName(string subscriptionName, string subject, MessageLane lane)
    {
        return NatsPhysicalAddress.Durable(lane, subscriptionName, subject);
    }

    internal static IReadOnlyList<string> BuildConsumerSubjects(
        IEnumerable<string> messageNames,
        ISet<string> shardedMessageNames
    )
    {
        return _BuildSubjects(messageNames, shardedMessageNames);
    }

    // A consumer filters on each message's own subject plus, for a sharded name, the 'base.>' wildcard. The stream it
    // reads from covers these: a derived stream carries the whole stream key (NatsStreamProvisioner.BuildStreamSubjects),
    // and a declared stream is chosen because its subjects cover the name.
    private static List<string> _BuildSubjects(IEnumerable<string> messageNames, ISet<string> shardedMessageNames)
    {
        Argument.IsNotNull(messageNames);
        Argument.IsNotNull(shardedMessageNames);

        var subjects = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var messageName in messageNames)
        {
            if (seen.Add(messageName))
            {
                subjects.Add(messageName);
            }

            if (shardedMessageNames.Contains(messageName))
            {
                var shardedSubject = $"{messageName}.>";
                if (seen.Add(shardedSubject))
                {
                    subjects.Add(shardedSubject);
                }
            }
        }

        return subjects;
    }

    // Removes subjects subsumed by a broader '.>' wildcard in the same set. JetStream rejects a stream config
    // whose subject list overlaps (e.g. "a.b.>" together with "a.b.foo"), so a union that mixes a catch-all
    // wildcard with the exact leaf subjects it already covers must be collapsed to the wildcard alone. A bare
    // token equal to the wildcard's base (e.g. "a.b" vs "a.b.>") is NOT subsumed and is kept.
    internal static List<string> PruneOverlappingSubjects(IEnumerable<string> subjects)
    {
        var all = subjects.Distinct(StringComparer.Ordinal).ToList();

        var wildcardBases = all.Where(s => s.EndsWith(".>", StringComparison.Ordinal))
            .Select(s => s[..^1]) // "a.b.>" -> "a.b."
            .ToList();

        if (wildcardBases.Count == 0)
        {
            return all;
        }

        return
        [
            .. all.Where(s =>
                !wildcardBases.Exists(prefix =>
                    !string.Equals(s, prefix + ">", StringComparison.Ordinal)
                    && s.StartsWith(prefix, StringComparison.Ordinal)
                )
            ),
        ];
    }

    public ValueTask SubscribeAsync(IEnumerable<string> messageNames, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNull(messageNames);
        cancellationToken.ThrowIfCancellationRequested();

        _subscribedMessageNames = messageNames.ToList();

        return ValueTask.CompletedTask;
    }

    public async ValueTask ListeningAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (kind is ConsumerSubscriptionKind.EveryInstance)
        {
            var everyInstance =
                _everyInstance
                ?? throw new InvalidOperationException("ConnectAsync must complete before the NATS consumer listens.");
            await everyInstance.ListenAsync(_subscribedMessageNames!, cancellationToken).ConfigureAwait(false);
            return;
        }

        using var listeningCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        // Group by the stream each name lives on (a declared stream, or the one derived from the name), so every
        // durable consumer is created on the stream that carries its subject.
        var streamGroups = _subscribedMessageNames!.GroupBy(
            x => _streamProvisioner.StreamName(lane, x),
            StringComparer.Ordinal
        );
        var tasks = new List<Task>();
        var startupTasks = new List<Task>();
        var tunedConsumer = _ResolveTunedConsumerConfig();

        foreach (var streamGroup in streamGroups)
        {
            var streamName = streamGroup.Key;
            var subscriptionName = name;
            var shardedMessageNames = ResolveShardedMessageNames(streamGroup);

            foreach (var logicalSubject in BuildConsumerSubjects(streamGroup, shardedMessageNames))
            {
                var durableName = BuildDurableName(subscriptionName, logicalSubject, lane);
                var subject = NatsPhysicalAddress.Subject(lane, logicalSubject);
                var deliverPolicy =
                    lane == MessageLane.Queue ? ConsumerConfigDeliverPolicy.All : ConsumerConfigDeliverPolicy.New;

                var consumerConfig = new ConsumerConfig(durableName)
                {
                    FilterSubject = subject,
                    DeliverPolicy = deliverPolicy,
                    AckWait = _DefaultAckWait,
                };

                // The consumer's own Tune settings are the most specific, so they win over the host-wide callback.
                _natsOptions.ConsumerOptions?.Invoke(consumerConfig);
                tunedConsumer?.ApplyTo(consumerConfig);

                if (
                    !string.Equals(consumerConfig.Name, durableName, StringComparison.Ordinal)
                    || !string.Equals(consumerConfig.FilterSubject, subject, StringComparison.Ordinal)
                    || consumerConfig.DeliverPolicy != deliverPolicy
                )
                {
                    throw new InvalidOperationException(
                        $"NATS {lane} consumer identity, subject, and delivery policy are provider-owned. "
                            + "ConsumerOptions may configure acknowledgement, retry, replica, and backoff settings but cannot override lane topology."
                    );
                }

                var startupReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                startupTasks.Add(startupReady.Task);
#pragma warning disable AsyncFixer04 // Every subject task is joined below, including the startup-failure path.
                tasks.Add(_ConsumeSubjectAsync(streamName, consumerConfig, startupReady, listeningCts.Token));
#pragma warning restore AsyncFixer04
            }
        }

        if (startupTasks.Count == 0)
        {
            Ready.TrySetResult();
        }
        else
        {
            try
            {
                await Task.WhenAll(startupTasks).ConfigureAwait(false);
            }
            catch
            {
                await listeningCts.CancelAsync().ConfigureAwait(false);

                // A sibling subject may already be using the linked token. Join all loops before the
                // token source leaves scope, while preserving the original startup failure.
                try
                {
                    await Task.WhenAll(tasks).ConfigureAwait(false);
                }
#pragma warning disable ERP022 // The outer startup exception is the actionable failure and is rethrown below.
                catch
                {
                    // ignored
                }
#pragma warning restore ERP022

                throw;
            }

            Ready.TrySetResult();
        }

        if (tasks.Count == 0)
        {
            return;
        }

        // A subject loop only completes on shutdown or an unrecoverable failure. Stop its sibling
        // loops when either happens so Task.WhenAll cannot hide the failure behind still-running subjects.
        _ = await Task.WhenAny(tasks).ConfigureAwait(false);
        await listeningCts.CancelAsync().ConfigureAwait(false);
        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private NatsConsumerConfig? _ResolveTunedConsumerConfig()
    {
        return serviceProvider.GetService<IConsumerRegistry>()?.ResolveConsumerConfig<NatsConsumerConfig>(name, lane);
    }

    internal HashSet<string> ResolveShardedMessageNames(IEnumerable<string> messageNames)
    {
        var names = messageNames.ToHashSet(StringComparer.Ordinal);

        var config = _ResolveTunedConsumerConfig();
        if (config?.IsSharded == true)
        {
            return names;
        }

        // A message whose contract shards its subject publishes to {subject}.{shard}, and NATS delivers nothing to a
        // filter that matches no shard subject, so a consumer of that message filters on the shard wildcard without
        // having to repeat the declaration.
        var sharded = new HashSet<string>(StringComparer.Ordinal);
        var metadata = serviceProvider.GetService<IMessageMetadataRegistry>();
        if (metadata is null)
        {
            return sharded;
        }

        foreach (var route in metadata.GetAll())
        {
            if (
                route.Route.Lane == lane
                && names.Contains(route.Route.MessageName)
                && route.ProviderConfigs.Values.OfType<IProviderHeaderContributions>().Any(IsSubjectSharded)
            )
            {
                sharded.Add(route.Route.MessageName);
            }
        }

        return sharded;

        static bool IsSubjectSharded(IProviderHeaderContributions contributions) =>
            contributions.HeaderContributions.Any(static header =>
                string.Equals(header.HeaderName, NatsMessagingHeaders.SubjectShard, StringComparison.Ordinal)
            );
    }

    public ValueTask WaitUntilReadyAsync(CancellationToken cancellationToken = default)
    {
        return new ValueTask(Ready.Task.WaitAsync(cancellationToken));
    }

    private async Task _ConsumeSubjectAsync(
        string streamName,
        ConsumerConfig consumerConfig,
        TaskCompletionSource startupReady,
        CancellationToken cancellationToken
    )
    {
        var retryDelay = TimeSpan.FromSeconds(1);
        var readyReported = false;
        var maxConsecutiveFailures = _natsOptions.MaxConsecutiveConsumeFailures;
        var ackWait = consumerConfig.AckWait;

        // Written by the consume loop and by NATS.Net's notification loop, so every access is interlocked.
        var consecutiveFailures = 0;

        // Escalates a run of consecutive consume-loop failures into a supervised restart. Only a delivered message
        // resets the counter: every failure rebinds the durable, so a bind proves nothing about the consume, and
        // resetting on it would let a consume that fails after each bind spin forever. A genuinely stuck loop — e.g. a
        // dead, non-reconnecting connection whose error is not one of the classified connection-failure types — trips
        // it. Surfaces a BrokerConnectionException (which the consumer register treats as a
        // terminal broker fault), faulting startupReady and logging the ConnectError itself so every call site
        // terminates identically.
        void RecordConsumeFailureOrThrow(Exception failure)
        {
            var failures = Interlocked.Increment(ref consecutiveFailures);
            if (failures < maxConsecutiveFailures)
            {
                return;
            }

            var terminal = new BrokerConnectionException(failure);

            startupReady.TrySetException(terminal);
            OnLogCallback?.Invoke(
                new LogMessageEventArgs
                {
                    LogType = MqLogType.ConnectError,
                    Reason = string.Create(
                        CultureInfo.InvariantCulture,
                        $"NATS consume loop for stream '{streamName}' failed {failures} times consecutively, terminating listener for supervised restart: {failure}"
                    ),
                }
            );

            throw terminal;
        }

        // NATS.Net re-pulls on its own after missed heartbeats, so a silent pull never ends the consume; counting each
        // timeout toward the streak is what hands a pull that stays silent back for a rebuild. An exception thrown here
        // ends the consume with that exception. A deleted consumer is not a notification: the consume throws, and the
        // loop below counts it and binds the durable again.
        Task OnConsumeNotificationAsync(INatsJSNotification notification, CancellationToken _)
        {
            OnLogCallback?.Invoke(
                new LogMessageEventArgs
                {
                    LogType = MqLogType.AsyncErrorEvent,
                    Reason = $"NATS consume notification for stream '{streamName}': {notification.Name}",
                }
            );

            if (notification is NatsJSTimeoutNotification)
            {
                RecordConsumeFailureOrThrow(
                    new TimeoutException($"NATS pull on stream '{streamName}' missed its idle heartbeats.")
                );
            }

            return Task.CompletedTask;
        }

        var consumeOpts = BuildConsumeOpts(groupConcurrent, OnConsumeNotificationAsync);

        // A disposed client stops for good: its connection is gone, so binding again could only fail.
        while (!cancellationToken.IsCancellationRequested && !IsDisposed)
        {
            try
            {
                var consumer = consumerFactory is not null
                    ? await consumerFactory(streamName, consumerConfig, cancellationToken).ConfigureAwait(false)
                    : await _jsContext!
                        .CreateOrUpdateConsumerAsync(streamName, consumerConfig, cancellationToken)
                        .ConfigureAwait(false);

                if (!readyReported)
                {
                    readyReported = true;
                    startupReady.TrySetResult();
                }

                while (!cancellationToken.IsCancellationRequested && !IsDisposed)
                {
                    await PauseGate.WaitIfPausedAsync(cancellationToken).ConfigureAwait(false);

                    using var receiveLease = AcquireReceiveLease(cancellationToken);

                    try
                    {
                        // DrainOnCancel: a pause or shutdown cancels the lease token, NATS.Net stops pulling and still
                        // yields what it already buffered, so every pulled message is settled below instead of
                        // waiting out its AckWait.
                        await foreach (
                            var msg in consumer
                                .ConsumeAsync(
                                    NatsRawSerializer<ReadOnlyMemory<byte>>.Default,
                                    consumeOpts,
                                    receiveLease.Token
                                )
                                .ConfigureAwait(false)
                        )
                        {
                            Interlocked.Exchange(ref consecutiveFailures, 0);
                            retryDelay = TimeSpan.FromSeconds(1);

                            if (receiveLease.Token.IsCancellationRequested)
                            {
                                await _ReleaseUndispatchedAsync(msg).ConfigureAwait(false);
                                continue;
                            }

                            await _DispatchMessageAsync(msg, ackWait, cancellationToken).ConfigureAwait(false);
                        }
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (OperationCanceledException) when (PauseGate.IsPaused)
                    {
                        continue;
                    }

                    if (cancellationToken.IsCancellationRequested || IsDisposed)
                    {
                        break;
                    }

                    if (receiveLease.Token.IsCancellationRequested)
                    {
                        // Drained for a pause; the gate holds the loop until a resume renews the lease.
                        continue;
                    }

                    // ConsumeAsync ends without cancellation only when the server ended the pull subscription.
                    throw new NatsJSException($"NATS consume on stream '{streamName}' ended without cancellation.");
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                startupReady.TrySetCanceled(cancellationToken);
                break;
            }
            catch (BrokerConnectionException)
            {
                // Raised by the consecutive-failure cap, which already faulted startupReady and logged the
                // ConnectError. The NATS SDK never throws this framework type, so catching it here uniquely
                // matches the cap signal; propagate so the listener terminates and the consumer register
                // rebuilds this client on a fresh connection.
                throw;
            }
            catch (Exception ex) when (_IsConnectionFailure(ex))
            {
                // Reconnect is deliberately owned by ConsumerRegister. Let the receive loop
                // terminate so its health watchdog can replace this failed client.
                startupReady.TrySetException(ex);
                OnLogCallback?.Invoke(
                    new LogMessageEventArgs
                    {
                        LogType = MqLogType.ConnectError,
                        Reason =
                            $"NATS connection failed for stream '{streamName}', terminating listener for supervised restart: {ex}",
                    }
                );

                throw;
            }
            catch (NatsJSApiException ex)
            {
                RecordConsumeFailureOrThrow(ex);

                OnLogCallback?.Invoke(
                    new LogMessageEventArgs
                    {
                        LogType = MqLogType.ConnectError,
                        Reason = $"JetStream API error for stream '{streamName}', will retry: {ex}",
                    }
                );

                retryDelay = NextBackoff(retryDelay, floor: TimeSpan.FromSeconds(5));
                await _timeProvider.Delay(retryDelay, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                RecordConsumeFailureOrThrow(ex);

                OnLogCallback?.Invoke(
                    new LogMessageEventArgs
                    {
                        LogType = MqLogType.ExceptionReceived,
                        Reason = $"Consumer error for stream '{streamName}', will retry: {ex}",
                    }
                );

                retryDelay = NextBackoff(retryDelay);
                await _timeProvider.Delay(retryDelay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// The pull settings of one subject's consume. A buffered message's <c>AckWait</c> runs before any in-progress signal
    /// covers it (those start when the loop reads the message), so the client never buffers more messages than its
    /// handlers take at once: one for a sequential client, the concurrency of a concurrent one. NATS.Net pulls again once
    /// the loop has read half the buffer, so handlers rarely wait on a pull.
    /// </summary>
    internal static NatsJSConsumeOpts BuildConsumeOpts(
        int concurrency,
        Func<INatsJSNotification, CancellationToken, Task>? notificationHandler
    )
    {
        var maxMsgs = Math.Max(1, concurrency);

        return new NatsJSConsumeOpts
        {
            MaxMsgs = maxMsgs,
            ThresholdMsgs = maxMsgs / 2,
            Expires = _PullExpires,
            IdleHeartbeat = _PullIdleHeartbeat,
            DrainOnCancel = true,
            NotificationHandler = notificationHandler,
        };
    }

    // A message pulled after the consume was told to stop goes straight back to the stream, so it redelivers now (to
    // this consumer after a resume, or to another instance) instead of after AckWait.
    private async Task _ReleaseUndispatchedAsync(INatsJSMsg<ReadOnlyMemory<byte>> msg)
    {
        try
        {
            await msg.NakAsync(cancellationToken: CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            OnLogCallback?.Invoke(
                new LogMessageEventArgs
                {
                    LogType = MqLogType.AsyncErrorEvent,
                    Reason = $"NATS message release on stop failed; it redelivers after AckWait: {ex}",
                }
            );
        }
    }

    private static bool _IsConnectionFailure(Exception exception)
    {
        return exception
            is NatsConnectionFailedException
                or NatsJSConnectionException
                or NatsException { InnerException: SocketException or IOException };
    }

    private ValueTask _DispatchMessageAsync(
        INatsJSMsg<ReadOnlyMemory<byte>> msg,
        TimeSpan ackWait,
        CancellationToken cancellationToken
    )
    {
        return DispatchEnvelopeAsync(msg.Subject, msg.Headers, msg.Data, msg, msg, cancellationToken, ackWait);
    }

    // A JetStream delivery is its own settlement token. A core delivery passes the core message instead, which
    // CommitAsync and RejectAsync ignore: an every-instance subscription neither acknowledges nor redelivers.
    internal async ValueTask DispatchEnvelopeAsync(
        string subject,
        NatsHeaders? natsHeaders,
        ReadOnlyMemory<byte> data,
        INatsJSMsg<ReadOnlyMemory<byte>>? jsMsg,
        object settlement,
        CancellationToken cancellationToken,
        TimeSpan ackWait = default
    )
    {
        // Reports the delivery in progress from receipt, so the wait for a handler slot counts as well as the handler.
#pragma warning disable CA2000 // False positive: every path below disposes it, the concurrent one inside the handler task it moves to.
        var delivery = jsMsg is null ? null : new NatsJSDelivery(jsMsg, ackWait, _timeProvider, _LogAckProgressFailure);
#pragma warning restore CA2000

        if (_semaphore is not null)
        {
            try
            {
                await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (delivery is not null)
            {
                await delivery.DisposeAsync().ConfigureAwait(false);
                await _ReleaseUndispatchedAsync(delivery.Msg).ConfigureAwait(false);
                throw;
            }

            var handlerTask = Task.Run(
                async () =>
                {
                    try
                    {
                        await _ProcessEnvelopeAsync(subject, natsHeaders, data, delivery, delivery ?? settlement)
                            .ConfigureAwait(false);
                    }
                    finally
                    {
                        if (delivery is not null)
                        {
                            await delivery.DisposeAsync().ConfigureAwait(false);
                        }

                        _ReleaseSemaphore();
                    }
                },
                CancellationToken.None // Ensure semaphore release even if cancellation is requested during handler execution
            );

            _TrackBackgroundHandler(handlerTask);
            _ObserveBackgroundHandler(handlerTask);
        }
        else
        {
            try
            {
                await _ProcessEnvelopeAsync(subject, natsHeaders, data, delivery, delivery ?? settlement)
                    .ConfigureAwait(false);
            }
            finally
            {
                if (delivery is not null)
                {
                    await delivery.DisposeAsync().ConfigureAwait(false);
                }
            }
        }
    }

    private void _LogAckProgressFailure(Exception exception)
    {
        OnLogCallback?.Invoke(
            new LogMessageEventArgs
            {
                LogType = MqLogType.AsyncErrorEvent,
                Reason = $"NATS in-progress acknowledgement failed: {exception}",
            }
        );
    }

    private void _TrackBackgroundHandler(Task task)
    {
        _inFlightHandlers[task] = 0;
        _ = task.ContinueWith(
            static (completed, state) => ((ConcurrentDictionary<Task, byte>)state!).TryRemove(completed, out _),
            _inFlightHandlers,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default
        );
    }

    private void _ObserveBackgroundHandler(Task task)
    {
        _ = task.ContinueWith(
            completedTask =>
            {
                var exception = completedTask.Exception?.GetBaseException();
                if (exception is not null)
                {
                    OnLogCallback?.Invoke(
                        new LogMessageEventArgs
                        {
                            LogType = MqLogType.ExceptionReceived,
                            Reason = $"Unhandled exception in concurrent message handler: {exception}",
                        }
                    );
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default
        );
    }

    /// <summary>
    /// Doubles the reconnect backoff, then returns a jittered delay within <c>[floor, 30s]</c>, so consumers that lose
    /// the connection at the same instant do not retry in lockstep. The AWS SQS, Pulsar and Redis transports all jitter
    /// their equivalent loops; <see cref="ReconnectBackoff.Jitter"/> explains the band.
    /// </summary>
    internal static TimeSpan NextBackoff(TimeSpan current, TimeSpan floor = default)
    {
        var next = TimeSpan.FromTicks(Math.Min(current.Ticks * 2, ReconnectBackoff.Ceiling.Ticks));
        return ReconnectBackoff.Jitter(next, floor, Random.Shared);
    }

    private async Task _ProcessEnvelopeAsync(
        string subject,
        NatsHeaders? natsHeaders,
        ReadOnlyMemory<byte> data,
        NatsJSDelivery? delivery,
        object settlement
    )
    {
        var jsMsg = delivery?.Msg;
        Dictionary<string, string?> headers;
        try
        {
            headers = NatsTransport.ReadHeaders(natsHeaders);
        }
        catch (Exception ex)
        {
            await _TerminallyAcknowledgeMalformedEnvelopeAsync(delivery, ex).ConfigureAwait(false);
            return;
        }

        if (_natsOptions.CustomHeadersBuilder is not null)
        {
            try
            {
                var metadata = jsMsg?.Metadata;
                var customHeaders = _natsOptions.CustomHeadersBuilder(metadata, natsHeaders, serviceProvider);
                foreach (var customHeader in customHeaders)
                {
                    headers[customHeader.Key] = customHeader.Value;
                }
            }
            catch (Exception ex)
            {
                OnLogCallback?.Invoke(
                    new LogMessageEventArgs
                    {
                        LogType = MqLogType.ConsumeError,
                        Reason = jsMsg is null
                            ? $"NATS custom headers builder failed; message dropped: {ex.GetType().Name}"
                            : $"NATS custom headers builder failed; message negatively acknowledged: {ex.GetType().Name}",
                    }
                );

                await RejectAsync(delivery).ConfigureAwait(false);
                return;
            }
        }

        // Stamped after the custom headers builder so neither the wire nor the builder can choose the address.
        headers[Headers.TransportAddress] = subject;

        TransportMessage message;
        try
        {
            _ValidateRequiredHeaders(headers);
            message = new TransportMessage(headers, data);
        }
        catch (Exception ex)
        {
            await _TerminallyAcknowledgeMalformedEnvelopeAsync(delivery, ex).ConfigureAwait(false);
            return;
        }

        // Let exceptions propagate. The framework's OnMessageCallback handler
        // calls CommitAsync on success and RejectAsync on failure.
        var onMessage =
            OnMessageCallback
            ?? throw new InvalidOperationException(
                "OnMessageCallback must be set before the NATS consumer client starts listening."
            );

        await onMessage(message, settlement).ConfigureAwait(false);
    }

    private async Task _TerminallyAcknowledgeMalformedEnvelopeAsync(NatsJSDelivery? delivery, Exception exception)
    {
        if (delivery is null)
        {
            // A core delivery is never redelivered, so dropping it is already terminal.
            OnLogCallback?.Invoke(
                new LogMessageEventArgs
                {
                    LogType = MqLogType.ConsumeError,
                    Reason = $"Malformed NATS transport envelope dropped: {exception.GetType().Name}",
                }
            );

            return;
        }

        OnLogCallback?.Invoke(
            new LogMessageEventArgs
            {
                LogType = MqLogType.ConsumeError,
                Reason = $"Malformed NATS transport envelope terminally acknowledged: {exception.GetType().Name}",
            }
        );

        try
        {
            await delivery.StopProgressAsync().ConfigureAwait(false);

            // A terminate, not an ack: the consumer's stats count the message as terminated and JetStream publishes a
            // MSG_TERMINATED advisory naming it, so an operator can find a poison message an ack would hide.
            await delivery
                .Msg.AckTerminateAsync(
                    new AckOpts
                    {
                        DoubleAck = true,
                        TerminateReason = TerminateReason(_connection?.ServerInfo?.Version, exception),
                    },
                    CancellationToken.None
                )
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            OnLogCallback?.Invoke(
                new LogMessageEventArgs
                {
                    LogType = MqLogType.AsyncErrorEvent,
                    Reason = $"NATS terminal acknowledgement of a malformed envelope failed: {ex}",
                }
            );
        }
    }

    /// <summary>
    /// Returns the reason a terminate carries, or <see langword="null"/> for a server that predates reasons. A server
    /// before 2.10.4 matches the exact <c>+TERM</c> bytes and silently ignores <c>+TERM reason</c>, so sending one there
    /// would leave the poison message to redeliver forever.
    /// </summary>
    internal static string? TerminateReason(string? serverVersion, Exception exception)
    {
        if (serverVersion is null)
        {
            return null;
        }

        // A pre-release suffix ("2.11.0-beta") does not change which acks the server parses.
        var dash = serverVersion.IndexOf('-', StringComparison.Ordinal);
        var core = dash < 0 ? serverVersion : serverVersion[..dash];

        return Version.TryParse(core, out var version) && version >= _TerminateReasonServerVersion
            ? $"malformed headless envelope: {exception.GetType().Name}"
            : null;
    }

    private static void _ValidateRequiredHeaders(Dictionary<string, string?> headers)
    {
        if (
            !headers.TryGetValue(Headers.MessageId, out var messageId)
            || string.IsNullOrWhiteSpace(messageId)
            || !headers.TryGetValue(Headers.MessageName, out var messageName)
            || string.IsNullOrWhiteSpace(messageName)
        )
        {
            throw new InvalidDataException("The NATS transport envelope is missing a required Messaging header.");
        }
    }

    public async ValueTask CommitAsync(object? sender, CancellationToken cancellationToken = default)
    {
        try
        {
            if (await _SettlingMessageAsync(sender).ConfigureAwait(false) is { } msg)
            {
                await msg.AckAsync(new AckOpts { DoubleAck = true }, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            OnLogCallback?.Invoke(
                new LogMessageEventArgs
                {
                    LogType = MqLogType.AsyncErrorEvent,
                    Reason = $"NATS message ACK failed: {ex}",
                }
            );
        }
    }

    public async ValueTask RejectAsync(object? sender, CancellationToken cancellationToken = default)
    {
        try
        {
            if (await _SettlingMessageAsync(sender).ConfigureAwait(false) is { } msg)
            {
                // The core rejects every delivery while the consumer's circuit is open or a half-open probe is out, and
                // JetStream redelivers a plain NAK on the next pull, so without a delay those deliveries spin.
                var delay = NakDelay(msg.Metadata?.NumDelivered ?? 1, Random.Shared);
                await msg.NakAsync(new AckOpts { NakDelay = delay }, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            OnLogCallback?.Invoke(
                new LogMessageEventArgs
                {
                    LogType = MqLogType.AsyncErrorEvent,
                    Reason = $"NATS message NAK failed: {ex}",
                }
            );
        }
    }

    // Stops a delivery's in-progress signals before it settles, so none reaches the server after the settlement.
    private static async ValueTask<INatsJSMsg<ReadOnlyMemory<byte>>?> _SettlingMessageAsync(object? sender)
    {
        switch (sender)
        {
            case NatsJSDelivery delivery:
                await delivery.StopProgressAsync().ConfigureAwait(false);
                return delivery.Msg;
            case INatsJSMsg<ReadOnlyMemory<byte>> msg:
                return msg;
            default:
                return null;
        }
    }

    /// <summary>
    /// The delay before JetStream redelivers a rejected delivery: about one second for the first delivery, doubling with
    /// each redelivery up to 30 seconds, and jittered so the deliveries rejected by one open circuit do not return
    /// together.
    /// </summary>
    /// <param name="numDelivered">How many times JetStream has delivered the message, this delivery included.</param>
    /// <param name="random">The jitter source.</param>
    internal static TimeSpan NakDelay(ulong numDelivered, Random random)
    {
        // Five doublings from one second already pass the 30 s ceiling.
        var doublings = (int)Math.Min(numDelivered <= 1 ? 0 : numDelivered - 1, 5);
        var nominal = TimeSpan.FromTicks(ReconnectBackoff.FirstDelay.Ticks << doublings);

        return ReconnectBackoff.Jitter(nominal, TimeSpan.Zero, random);
    }

    private void _ReleaseSemaphore()
    {
        if (_semaphore is null)
        {
            return;
        }

        try
        {
            _semaphore.Release();
        }
        catch (SemaphoreFullException)
        {
            // Defensive: ignore over-release.
        }
        catch (ObjectDisposedException)
        {
            // Shutdown in progress. Semaphore already disposed.
        }
    }

    public async ValueTask PauseAsync(CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        if (!await PauseGate.PauseAsync().ConfigureAwait(false))
        {
            return;
        }

        await _CancelReceives().ConfigureAwait(false);
    }

    public async ValueTask ResumeAsync(CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        if (!PauseGate.IsPaused)
        {
            return;
        }

        _ResetReceiveToken();

        if (!await PauseGate.ResumeAsync().ConfigureAwait(false))
        {
            return;
        }
    }

    public ValueTask DisposeAsync()
    {
        return ShutdownAsync(_ShutdownDrainTimeout);
    }

    public async ValueTask ShutdownAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        PauseGate.Release();
        Ready.TrySetCanceled(CancellationToken.None);
        await _CancelReceives().ConfigureAwait(false);

        // Drain in-flight concurrent handlers before disposing the semaphore and connection, so a
        // running handler does not Ack/Nak on a disposed connection. Bounded so a stuck handler cannot
        // block shutdown indefinitely; any handler still running past the budget has its Ack/Nak
        // swallowed and the message is redelivered (at-least-once).
        var inFlight = _inFlightHandlers.Keys.ToArray();
        if (inFlight.Length > 0)
        {
            try
            {
                if (timeout <= TimeSpan.Zero)
                {
                    throw new TimeoutException("The shared messaging shutdown deadline has expired.");
                }

                await Task.WhenAll(inFlight)
                    .WaitAsync(timeout, _timeProvider, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Handler faults are already surfaced via _ObserveBackgroundHandler; on a drain timeout
                // or fault, log and proceed — disposal must never block or throw.
                OnLogCallback?.Invoke(
                    new LogMessageEventArgs
                    {
                        LogType = MqLogType.ExceptionReceived,
                        Reason = $"Timed out or faulted draining in-flight handlers during shutdown: {ex}",
                    }
                );
            }
        }

        ReceiveTokenState? receiveTokenStateToDispose = null;
        lock (_receiveLock)
        {
            _receiveTokenState.Retired = true;
            if (_receiveTokenState.RefCount == 0)
            {
                receiveTokenStateToDispose = _receiveTokenState;
            }
        }

        receiveTokenStateToDispose?.Dispose();
        _semaphore?.Dispose();

        if (_connection is { } connection)
        {
            // A tokenless NATS connection attempt cannot be canceled. Do not make caller cancellation
            // wait for it, but retain ownership until it settles so disposal cannot race socket setup.
            if (_connectTask?.IsCompleted != false)
            {
                await _DisposeConnectionAsync(connection).ConfigureAwait(false);
            }
            else
            {
                _DisposeConnectionAfterConnectAsync(connection, _connectTask).Forget();
            }
        }
    }

    private async Task _DisposeConnectionAfterConnectAsync(NatsConnection connection, Task? connectTask)
    {
        if (connectTask is not null)
        {
            try
            {
                await connectTask.ConfigureAwait(false);
            }
#pragma warning disable ERP022 // The original ConnectAsync caller observes the failure; cleanup must continue.
            catch
            {
                // The original ConnectAsync caller observes the connection failure or cancellation.
                // Disposal still owns the connection and must run after the attempt reaches a terminal state.
            }
#pragma warning restore ERP022
        }

        try
        {
            await _DisposeConnectionAsync(connection).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            OnLogCallback?.Invoke(
                new LogMessageEventArgs
                {
                    LogType = MqLogType.ExceptionReceived,
                    Reason = $"Failed to dispose NATS connection after connection attempt settled: {ex}",
                }
            );
        }
    }

    private ValueTask _DisposeConnectionAsync(NatsConnection connection)
    {
        return disposeConnection?.Invoke(connection) ?? connection.DisposeAsync();
    }

    internal ReceiveTokenLease AcquireReceiveLease(CancellationToken cancellationToken)
    {
        ReceiveTokenState receiveTokenState;
        lock (_receiveLock)
        {
            receiveTokenState = _receiveTokenState;
            receiveTokenState.RefCount++;
        }

        try
        {
            return new ReceiveTokenLease(this, receiveTokenState, cancellationToken);
        }
        catch
        {
            _ReleaseReceiveTokenState(receiveTokenState);
            throw;
        }
    }

    private async Task _CancelReceives()
    {
        ReceiveTokenState receiveTokenState;
        lock (_receiveLock)
        {
            receiveTokenState = _receiveTokenState;
        }

        try
        {
            await receiveTokenState.Source.CancelAsync().ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            // Shutdown already disposed the receive CTS.
        }
    }

    private void _ResetReceiveToken()
    {
        ReceiveTokenState? receiveTokenStateToDispose = null;
        lock (_receiveLock)
        {
            var previousState = _receiveTokenState;
            previousState.Retired = true;
            _receiveTokenState = new ReceiveTokenState();

            if (previousState.RefCount == 0)
            {
                receiveTokenStateToDispose = previousState;
            }
        }

        receiveTokenStateToDispose?.Dispose();
    }

    private void _ReleaseReceiveTokenState(ReceiveTokenState receiveTokenState)
    {
        bool shouldDispose;

        lock (_receiveLock)
        {
            receiveTokenState.RefCount--;
            shouldDispose = receiveTokenState is { RefCount: 0, Retired: true };
        }

        if (shouldDispose)
        {
            receiveTokenState.Dispose();
        }
    }

    internal sealed class ReceiveTokenState : IDisposable
    {
        private readonly Lock _lock = new();
        private CancellationTokenSource? _linkedSource;
        private CancellationToken _lastParentToken;

        public CancellationTokenSource Source { get; } = new();

        public int RefCount { get; set; }

        public bool Retired { get; set; }

        public CancellationToken GetLinkedToken(CancellationToken parentToken)
        {
            if (parentToken == CancellationToken.None)
            {
                return Source.Token;
            }

            lock (_lock)
            {
                if (_linkedSource == null || _lastParentToken != parentToken)
                {
                    _linkedSource?.Dispose();
                    _linkedSource = CancellationTokenSource.CreateLinkedTokenSource(parentToken, Source.Token);
                    _lastParentToken = parentToken;
                }

                return _linkedSource.Token;
            }
        }

        public void Dispose()
        {
            lock (_lock)
            {
                _linkedSource?.Dispose();
                _linkedSource = null;
            }

            Source.Dispose();
        }
    }

    internal sealed class ReceiveTokenLease(
        NatsConsumerClient owner,
        ReceiveTokenState receiveTokenState,
        CancellationToken cancellationToken
    ) : IDisposable
    {
        private int _disposed;

        public CancellationToken Token { get; } = receiveTokenState.GetLinkedToken(cancellationToken);

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            owner._ReleaseReceiveTokenState(receiveTokenState);
        }
    }
}
