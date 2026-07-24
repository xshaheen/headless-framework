// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Net.Sockets;
using Headless.Checks;
using Headless.Messaging.Exceptions;
using Headless.Messaging.Internal;
using Headless.Messaging.Runtime;
using Headless.Messaging.Transport;
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
    IntentType intentType = IntentType.Bus,
    TimeProvider? timeProvider = null,
    Func<NatsConnection, Task>? connect = null,
    Func<NatsConnection, ValueTask>? disposeConnection = null
) : IConsumerClient
{
    private readonly Lock _receiveLock = new();
    private readonly NatsMessagingOptions _natsOptions = Argument.IsNotNull(options.Value);
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    private readonly SemaphoreSlim? _semaphore = groupConcurrent > 0 ? new SemaphoreSlim(groupConcurrent) : null;

    // Tracks in-flight fire-and-forget handler tasks on the concurrent (groupConcurrent > 0) path so
    // DisposeAsync can drain them before disposing the semaphore and connection.
    private readonly ConcurrentDictionary<Task, byte> _inFlightHandlers = new();

    // Bounded drain budget on shutdown. Aligned with the default AckWait (30s): a handler still
    // running past this would have its message redelivered by JetStream anyway (at-least-once).
    private static readonly TimeSpan _ShutdownDrainTimeout = TimeSpan.FromSeconds(30);

    private readonly ConsumerPauseGate _pauseGate = new();
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);

#pragma warning disable CA2213 // Disposal is deferred until the tokenless SDK connection attempt settles.
    private NatsConnection? _connection;
#pragma warning restore CA2213
    private Task? _connectTask;
    private NatsJSContext? _jsContext;
    private ReceiveTokenState _receiveTokenState = new();
    private IEnumerable<string>? _subscribedMessageNames;
    private int _disposed;

    public Func<TransportMessage, object?, Task>? OnMessageCallback { get; set; }

    public Action<LogMessageEventArgs>? OnLogCallback { get; set; }

    public void AttachCallbacks(Func<TransportMessage, object?, Task>? onMessage, Action<LogMessageEventArgs>? onLog)
    {
        OnMessageCallback = onMessage;
        OnLogCallback = onLog;
    }

    public BrokerAddress BrokerAddress => new("nats", BrokerAddressDisplay.FormatMany(_natsOptions.Servers));

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        // Consumer connections disable client-side reconnect (MaxReconnectRetry = 0): a genuine connection
        // failure surfaces out of the consume loop and terminates the listener, so the consumer register's
        // health watchdog rebuilds it on a fresh connection instead of the NATS client silently retrying on a
        // possibly-stale socket. The circuit breaker is per-message and never observes connection-level faults.
        var opts = _natsOptions.BuildNatsOpts() with
        {
            MaxReconnectRetry = 0,
        };

        var connection = new NatsConnection(opts);
        _connection = connection;
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
        // Materialize once: the source is consumed by GroupBy and the return value, so a lazy
        // input would otherwise be enumerated twice.
        var names = messageNames.AsIReadOnlyList();

        if (!_natsOptions.EnableSubscriberClientStreamAndSubjectCreation)
        {
            return [.. names];
        }

        // Preserve wildcard coverage for hierarchical subjects, but add exact
        // subjects for bare/non-prefix messageNames that the wildcard cannot match.
        var streamGroups = names.GroupBy(x => _natsOptions.NormalizeStreamName(x), StringComparer.Ordinal);

        foreach (var streamGroup in streamGroups)
        {
            var subjects = new HashSet<string>(
                BuildStreamSubjects(streamGroup, _ResolveShardedMessageNames(streamGroup)),
                StringComparer.Ordinal
            );

            using var cts = _natsOptions.StreamCreateTimeout.ToCancellationTokenSource(cancellationToken);

            // Several consumer groups can normalize to the same stream name, and each group only knows its
            // own subjects. CreateOrUpdateStream REPLACES the subject list, so union with whatever the stream
            // already carries (from an earlier group, or a pre-provisioned stream) to avoid clobbering them.
            try
            {
                var existing = await _jsContext!
                    .GetStreamAsync(streamGroup.Key, cancellationToken: cts.Token)
                    .ConfigureAwait(false);

                if (existing.Info.Config.Subjects is { } existingSubjects)
                {
                    subjects.UnionWith(existingSubjects);
                }
            }
            catch (NatsJSApiException ex) when (ex.Error.Code == 404)
            {
                // Stream does not exist yet; it will be created below.
            }

            var config = new StreamConfig
            {
                Name = streamGroup.Key,
                // JetStream rejects a stream whose subject list contains overlapping entries, so drop any
                // exact subject already covered by a '.>' wildcard in the union (e.g. a pre-provisioned
                // 'prefix.>' catch-all subsumes the exact 'prefix.foo' subjects).
                Subjects = [.. _PruneOverlappingSubjects(subjects)],
                NoAck = false,
                // File storage is the production default. Override via StreamOptions
                // for dev/testing: config.Storage = StreamConfigStorage.Memory;
                Storage = StreamConfigStorage.File,
            };

            _natsOptions.StreamOptions?.Invoke(config);

            await _jsContext!.CreateOrUpdateStreamAsync(config, cts.Token).ConfigureAwait(false);
        }

        return [.. names];
    }

    internal static IReadOnlyList<string> BuildStreamSubjects(
        IEnumerable<string> messageNames,
        ISet<string> shardedMessageNames
    )
    {
        return _BuildSubjects(messageNames, shardedMessageNames);
    }

    internal static string BuildDurableName(string groupName, string subject, IntentType intentType)
    {
        return intentType == IntentType.Queue
            ? TransportNaming.Normalize("queue-" + subject)
            : TransportNaming.Normalize(groupName + "-" + subject);
    }

    internal static IReadOnlyList<string> BuildConsumerSubjects(
        IEnumerable<string> messageNames,
        ISet<string> shardedMessageNames
    )
    {
        return _BuildSubjects(messageNames, shardedMessageNames);
    }

    // The JetStream stream config and the consumer FilterSubjects must cover exactly the same subject
    // set, so both derive from one method: the base subject plus, for sharded names, the 'base.>'
    // wildcard, de-duplicated. (Verified output-equivalent to the prior two near-duplicate methods.)
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
    private static List<string> _PruneOverlappingSubjects(IEnumerable<string> subjects)
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
        using var listeningCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var streamGroups = _subscribedMessageNames!.GroupBy(
            x => _natsOptions.NormalizeStreamName(x),
            StringComparer.Ordinal
        );
        var tasks = new List<Task>();
        var startupTasks = new List<Task>();

        foreach (var streamGroup in streamGroups)
        {
            var groupName = TransportNaming.Normalize(name);
            var shardedMessageNames = _ResolveShardedMessageNames(streamGroup);

            foreach (var subject in BuildConsumerSubjects(streamGroup, shardedMessageNames))
            {
                var durableName = BuildDurableName(groupName, subject, intentType);

                var consumerConfig = new ConsumerConfig(durableName)
                {
                    FilterSubject = subject,
                    DeliverPolicy = ConsumerConfigDeliverPolicy.New,
                    AckWait = TimeSpan.FromSeconds(30),
                };

                _natsOptions.ConsumerOptions?.Invoke(consumerConfig);

                var startupReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                startupTasks.Add(startupReady.Task);
#pragma warning disable AsyncFixer04 // Every subject task is joined below, including the startup-failure path.
                tasks.Add(
                    _ConsumeSubjectAsync(streamGroup.Key, consumerConfig, timeout, startupReady, listeningCts.Token)
                );
#pragma warning restore AsyncFixer04
            }
        }

        if (startupTasks.Count == 0)
        {
            _ready.TrySetResult();
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

            _ready.TrySetResult();
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

    private HashSet<string> _ResolveShardedMessageNames(IEnumerable<string> messageNames)
    {
        var consumerRegistry = serviceProvider.GetService<IConsumerRegistry>();
        if (consumerRegistry is null)
        {
            return [];
        }

        var config = consumerRegistry.ResolveConsumerConfig<NatsConsumerConfig>(name, intentType);
        if (config?.IsSharded != true)
        {
            return [];
        }

        return messageNames.ToHashSet(StringComparer.Ordinal);
    }

    public ValueTask WaitUntilReadyAsync(CancellationToken cancellationToken = default)
    {
        return new ValueTask(_ready.Task.WaitAsync(cancellationToken));
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
                    await _pauseGate.WaitIfPausedAsync(cancellationToken).ConfigureAwait(false);

                    using var receiveLease = _AcquireReceiveLease(cancellationToken);

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
                    catch (OperationCanceledException) when (_pauseGate.IsPaused)
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

    private async ValueTask _DispatchMessageAsync(
        INatsJSMsg<ReadOnlyMemory<byte>> msg,
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
                        await _ProcessMessageAsync(msg).ConfigureAwait(false);
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
            await _ProcessMessageAsync(msg).ConfigureAwait(false);
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
    /// Doubles the reconnect backoff, then returns a jittered delay within <c>[floor, 30s]</c>.
    /// </summary>
    /// <remarks>
    /// Jitter is not cosmetic here: without it, a fleet of consumers that all lose the connection at the same
    /// instant (a broker restart) retries in lockstep at exactly 1s/2s/4s/8s/16s/30s and hammers the broker in
    /// synchronized waves. The AWS SQS, Pulsar and Redis transports all jitter their equivalent loops.
    /// <para>
    /// Three properties must hold together, and the obvious implementations each break one of them. Adding jitter
    /// on top of the capped value overshoots the 30s ceiling. Clamping that additive result back down to the
    /// ceiling collapses every caller to exactly 30s once the exponential curve saturates — killing the spread at
    /// precisely the moment the herd is largest. Subtracting jitter unconditionally undercuts the <paramref
    /// name="floor"/>, which callers use to guarantee a minimum wait.
    /// </para>
    /// <para>
    /// So the jitter band is computed explicitly and is always non-degenerate: normally it spreads DOWN from the
    /// capped value (<c>[0.75x, 1x]</c>), and when the floor is what pins the delay — leaving no room below — it
    /// spreads UP from the floor instead, still bounded by the ceiling. The result is therefore never above 30s,
    /// never below the floor, and never a single lockstep value. That last case matters: a JetStream API error
    /// hits every consumer at once, so the floor path is itself a herd and needs the spread as much as the
    /// reconnect path does.
    /// </para>
    /// </remarks>
    internal static TimeSpan NextBackoff(TimeSpan current, TimeSpan floor = default)
    {
        var ceiling = TimeSpan.FromSeconds(30);
        var next = TimeSpan.FromTicks(Math.Min(current.Ticks * 2, ceiling.Ticks));
        var capped = floor > next ? floor : next;

        var jitterBudget = TimeSpan.FromTicks(capped.Ticks / 4);
        var lower = capped - jitterBudget < floor ? floor : capped - jitterBudget;

        // When the floor pins the delay there is no room to jitter downward, so spread upward instead — still
        // capped by the ceiling, so the "never above 30s" guarantee holds in every branch.
        var upper =
            lower < capped ? capped
            : capped + jitterBudget > ceiling ? ceiling
            : capped + jitterBudget;

        if (upper <= lower)
        {
            return lower;
        }

#pragma warning disable CA5394 // Non-security jitter for retry backoff; cryptographic RNG is unnecessary here.
        var offsetMs = Random.Shared.Next(0, (int)Math.Max(1, (upper - lower).TotalMilliseconds));
#pragma warning restore CA5394

        return lower + TimeSpan.FromMilliseconds(offsetMs);
    }

    private async Task _ProcessMessageAsync(INatsJSMsg<ReadOnlyMemory<byte>> msg)
    {
        TransportMessage message;
        try
        {
            var headers = new Dictionary<string, string?>(StringComparer.Ordinal);

            if (msg.Headers is { Count: > 0 } natsHeaders)
            {
                foreach (var (key, values) in natsHeaders)
                {
                    headers[key] = values.Count > 0 ? values[0] : null;
                }
            }

            headers[Headers.Group] = name;

            if (_natsOptions.CustomHeadersBuilder is not null)
            {
                var metadata = msg.Metadata;
                var customHeaders = _natsOptions.CustomHeadersBuilder(metadata, msg.Headers, serviceProvider);
                foreach (var customHeader in customHeaders)
                {
                    headers[customHeader.Key] = customHeader.Value;
                }
            }

            message = new TransportMessage(headers, msg.Data);
        }
        catch (Exception ex)
        {
            OnLogCallback?.Invoke(
                new LogMessageEventArgs
                {
                    LogType = MqLogType.ConsumeError,
                    Reason = $"Failed to build transport message, nacking: {ex}",
                }
            );

            await RejectAsync(msg).ConfigureAwait(false);
            return;
        }

        // Let exceptions propagate. The framework's OnMessageCallback handler
        // calls CommitAsync on success and RejectAsync on failure.
        var onMessage =
            OnMessageCallback
            ?? throw new InvalidOperationException(
                "OnMessageCallback must be set before the NATS consumer client starts listening."
            );

        await onMessage(message, msg).ConfigureAwait(false);
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

        if (!await _pauseGate.PauseAsync().ConfigureAwait(false))
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

        if (!_pauseGate.IsPaused)
        {
            return;
        }

        _ResetReceiveToken();

        if (!await _pauseGate.ResumeAsync().ConfigureAwait(false))
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

        _pauseGate.Release();
        _ready.TrySetCanceled(CancellationToken.None);
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
#pragma warning disable VSTHRD003 // This client owns and drains the SDK task started by ConnectAsync.
                await connectTask.ConfigureAwait(false);
#pragma warning restore VSTHRD003
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

    private ReceiveTokenLease _AcquireReceiveLease(CancellationToken cancellationToken)
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

    private sealed class ReceiveTokenState : IDisposable
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

    private sealed class ReceiveTokenLease(
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
