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
                    AckWait = TimeSpan.FromSeconds(30),
                };

                _natsOptions.ConsumerOptions?.Invoke(consumerConfig);

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
                tasks.Add(_ConsumeSubjectAsync(streamName, consumerConfig, timeout, startupReady, listeningCts.Token));
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

    internal HashSet<string> ResolveShardedMessageNames(IEnumerable<string> messageNames)
    {
        var names = messageNames.ToHashSet(StringComparer.Ordinal);

        var config = serviceProvider
            .GetService<IConsumerRegistry>()
            ?.ResolveConsumerConfig<NatsConsumerConfig>(name, lane);
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
        TimeSpan timeout,
        TaskCompletionSource startupReady,
        CancellationToken cancellationToken
    )
    {
        var retryDelay = TimeSpan.FromSeconds(1);
        var nextOpts = timeout > TimeSpan.Zero ? new NatsJSNextOpts { Expires = timeout } : null;
        var readyReported = false;
        var maxConsecutiveFailures = _natsOptions.MaxConsecutiveConsumeFailures;
        var consecutiveFailures = 0;

        // Escalates a run of consecutive consume-loop failures into a supervised restart. The counter resets
        // on any forward progress (a successful consumer bind or fetch), so only a genuinely stuck loop — e.g.
        // a dead, non-reconnecting connection whose error is not one of the classified connection-failure
        // types — ever trips it. Surfaces a BrokerConnectionException (which the consumer register treats as a
        // terminal broker fault), faulting startupReady and logging the ConnectError itself so both the
        // inner-loop and outer-loop call sites terminate identically.
        void RecordConsumeFailureOrThrow(Exception failure)
        {
            if (++consecutiveFailures < maxConsecutiveFailures)
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
                        $"NATS consume loop for stream '{streamName}' failed {consecutiveFailures} times consecutively, terminating listener for supervised restart: {failure}"
                    ),
                }
            );

            throw terminal;
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var consumer = consumerFactory is not null
                    ? await consumerFactory(streamName, consumerConfig, cancellationToken).ConfigureAwait(false)
                    : await _jsContext!
                        .CreateOrUpdateConsumerAsync(streamName, consumerConfig, cancellationToken)
                        .ConfigureAwait(false);

                // Binding the consumer is forward progress: clear any failure streak so a single later
                // fetch blip cannot inherit an almost-tripped counter and force a spurious restart.
                consecutiveFailures = 0;

                if (!readyReported)
                {
                    readyReported = true;
                    startupReady.TrySetResult();
                }

                while (!cancellationToken.IsCancellationRequested)
                {
                    INatsJSMsg<ReadOnlyMemory<byte>>? msg;
                    await PauseGate.WaitIfPausedAsync(cancellationToken).ConfigureAwait(false);

                    using var receiveLease = AcquireReceiveLease(cancellationToken);

                    try
                    {
                        msg = await consumer
                            .NextAsync(
                                serializer: NatsRawSerializer<ReadOnlyMemory<byte>>.Default,
                                opts: nextOpts,
                                cancellationToken: receiveLease.Token
                            )
                            .ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (OperationCanceledException) when (PauseGate.IsPaused)
                    {
                        continue;
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
                        continue;
                    }
                    catch (Exception ex) when (_IsConnectionFailure(ex))
                    {
                        // Reconnect is deliberately owned by ConsumerRegister. Let the receive loop
                        // terminate so its health watchdog can replace this failed client.
                        throw;
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
                        continue;
                    }

                    // A returned fetch (a message or an Expires heartbeat) proves the connection is alive.
                    consecutiveFailures = 0;
                    retryDelay = TimeSpan.FromSeconds(1);

                    if (msg is null)
                    {
                        continue;
                    }

                    await _DispatchMessageAsync(msg, cancellationToken).ConfigureAwait(false);
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

    private static bool _IsConnectionFailure(Exception exception)
    {
        return exception
            is NatsConnectionFailedException
                or NatsJSConnectionException
                or NatsException { InnerException: SocketException or IOException };
    }

    private ValueTask _DispatchMessageAsync(INatsJSMsg<ReadOnlyMemory<byte>> msg, CancellationToken cancellationToken)
    {
        return DispatchEnvelopeAsync(msg.Subject, msg.Headers, msg.Data, msg, msg, cancellationToken);
    }

    // A JetStream delivery is its own settlement token. A core delivery passes the core message instead, which
    // CommitAsync and RejectAsync ignore: an every-instance subscription neither acknowledges nor redelivers.
    internal async ValueTask DispatchEnvelopeAsync(
        string subject,
        NatsHeaders? natsHeaders,
        ReadOnlyMemory<byte> data,
        INatsJSMsg<ReadOnlyMemory<byte>>? jsMsg,
        object settlement,
        CancellationToken cancellationToken
    )
    {
        if (_semaphore is not null)
        {
            await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

            var handlerTask = Task.Run(
                async () =>
                {
                    try
                    {
                        await _ProcessEnvelopeAsync(subject, natsHeaders, data, jsMsg, settlement)
                            .ConfigureAwait(false);
                    }
                    finally
                    {
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
            await _ProcessEnvelopeAsync(subject, natsHeaders, data, jsMsg, settlement).ConfigureAwait(false);
        }
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
        INatsJSMsg<ReadOnlyMemory<byte>>? jsMsg,
        object settlement
    )
    {
        Dictionary<string, string?> headers;
        try
        {
            headers = NatsTransport.ReadHeaders(natsHeaders);
        }
        catch (Exception ex)
        {
            await _TerminallyAcknowledgeMalformedEnvelopeAsync(jsMsg, ex).ConfigureAwait(false);
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

                await RejectAsync(jsMsg).ConfigureAwait(false);
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
            await _TerminallyAcknowledgeMalformedEnvelopeAsync(jsMsg, ex).ConfigureAwait(false);
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

    private async Task _TerminallyAcknowledgeMalformedEnvelopeAsync(
        INatsJSMsg<ReadOnlyMemory<byte>>? jsMsg,
        Exception exception
    )
    {
        if (jsMsg is null)
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

        await jsMsg.AckAsync(new AckOpts { DoubleAck = true }, CancellationToken.None).ConfigureAwait(false);
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
            if (sender is INatsJSMsg<ReadOnlyMemory<byte>> msg)
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
            if (sender is INatsJSMsg<ReadOnlyMemory<byte>> msg)
            {
                await msg.NakAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
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
