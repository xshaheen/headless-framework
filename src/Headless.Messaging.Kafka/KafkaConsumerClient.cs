// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Headless.Checks;
using Headless.Messaging.Transport;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.Kafka;

internal sealed class KafkaConsumerClient : IConsumerClient
{
    // Kafka does not restrict group.id, so the group takes the topic naming rules (up to 249 of [a-zA-Z0-9._-]): a Queue
    // message name, which is already a legal topic, becomes its consumer group unchanged on every host.
    private static readonly BusNameRules _GroupIdRules = new(
        maxLength: 249,
        isAllowed: static c => c is '.' or '_' or '-'
    );

    // Bounded drain budget on shutdown, matching the other transports. A handler still running past it never stores
    // its offset, so Kafka redelivers that record to the next owner of the partition (at-least-once).
    private static readonly TimeSpan _ShutdownDrainTimeout = TimeSpan.FromSeconds(30);

    // How long a revoke waits for in-flight handlers on the partitions it takes away. The group waits for this member
    // up to max.poll.interval.ms (five minutes by default), so a bounded wait well under that keeps the rebalance
    // alive; a handler still running past it loses its offset and the new owner redelivers the record.
    private static readonly TimeSpan _RevokeDrainTimeout = TimeSpan.FromSeconds(30);

    private readonly string _groupId;

    // Guards offset tracking, partition ownership, and the consumer reference. Never held across Consume, so a
    // handler can store its offset while the poll thread blocks in a rebalance callback waiting for that handler.
    private readonly Lock _lock = new();

    // Serializes Consume with Close and Dispose, which librdkafka does not allow to run concurrently. Rebalance
    // callbacks run inside Consume and Close, so they always execute while this lock is held.
    private readonly Lock _consumeLock = new();
    private readonly KafkaMessagingOptions _kafkaOptions;
    private readonly ConsumerPauseGate _pauseGate = new();
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly SemaphoreSlim? _semaphore;
    private readonly IServiceProvider _serviceProvider;
    private readonly KafkaConsumerConfig? _consumerConfig;
    private readonly Func<ConsumerConfig, IConsumer<string, byte[]>> _consumerFactory;
    private readonly Func<AdminClientConfig, IAdminClient> _adminClientFactory;
    private readonly KafkaOffsetCommitTracker? _offsetCommitTracker;
    private readonly HashSet<TopicPartition> _ownedPartitions = [];

    // Every dispatched delivery, keyed by its handler task, so shutdown and a revoke can wait for the ones they affect.
    private readonly InFlightHandlerTracker _inFlightHandlers = new();
    private bool _hasPartitionAssignment;

    // volatile is required: Connect performs double-checked locking on this field. Without volatile a reader could
    // observe a non-null reference whose publication has not yet been flushed by the writer.
    private volatile IConsumer<string, byte[]>? _consumerClient;
    private int _disposed;

    public KafkaConsumerClient(
        string subscriptionName,
        byte groupConcurrent,
        IOptions<KafkaMessagingOptions> options,
        IServiceProvider serviceProvider,
        KafkaConsumerConfig? consumerConfig = null,
        Func<ConsumerConfig, IConsumer<string, byte[]>>? consumerFactory = null,
        Func<AdminClientConfig, IAdminClient>? adminClientFactory = null
    )
    {
        _groupId = GroupId(subscriptionName);
        _kafkaOptions = Argument.IsNotNull(options.Value);
        if (groupConcurrent > 1)
        {
            _semaphore = new SemaphoreSlim(groupConcurrent, groupConcurrent);
            _offsetCommitTracker = new KafkaOffsetCommitTracker();
        }

        _serviceProvider = serviceProvider;
        _consumerConfig = consumerConfig;
        _consumerFactory = consumerFactory ?? _BuildConsumer;
        _adminClientFactory = adminClientFactory ?? _BuildAdminClient;
    }

    /// <summary>Returns the consumer group of a Queue subscription, which is named after its message.</summary>
    internal static string GroupId(string subscriptionName) => BusNameBuilder.Build(subscriptionName, _GroupIdRules);

    public Func<TransportMessage, object?, Task>? OnMessageCallback { get; set; }

    public Action<LogMessageEventArgs>? OnLogCallback { get; set; }

    public void AttachCallbacks(Func<TransportMessage, object?, Task>? onMessage, Action<LogMessageEventArgs>? onLog)
    {
        OnMessageCallback = onMessage;
        OnLogCallback = onLog;
    }

    public BrokerAddress BrokerAddress => new("kafka", BrokerAddressDisplay.FormatMany(_kafkaOptions.Servers));

    public async ValueTask<ICollection<string>> FetchMessageNamesAsync(
        IEnumerable<string> messageNames,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(messageNames);

        var normalizedTopics = new List<string>();
        var concreteTopicsToCreate = new List<string>();

        foreach (var topicName in messageNames)
        {
            if (topicName.Contains('*', StringComparison.Ordinal) || topicName.Contains('#', StringComparison.Ordinal))
            {
                normalizedTopics.Add(TransportNaming.WildcardToRegex(topicName));
                continue;
            }

            normalizedTopics.Add(topicName);
            concreteTopicsToCreate.Add(topicName);
        }

        var allowAutoCreate = true;
        if (
            _kafkaOptions.MainConfig.TryGetValue("allow.auto.create.topics", out var autoCreateValue)
            && bool.TryParse(autoCreateValue, out var parsedValue)
        )
        {
            allowAutoCreate = parsedValue;
        }

        if (allowAutoCreate && concreteTopicsToCreate.Count > 0)
        {
            try
            {
                var config = new AdminClientConfig(_kafkaOptions.MainConfig)
                {
                    BootstrapServers = _kafkaOptions.Servers,
                };

                using var adminClient = _adminClientFactory(config);

                await adminClient
                    .CreateTopicsAsync(
                        concreteTopicsToCreate.Select(x => new TopicSpecification
                        {
                            Name = x,
                            NumPartitions = _kafkaOptions.TopicOptions.NumPartitions,
                            ReplicationFactor = _kafkaOptions.TopicOptions.ReplicationFactor,
                        })
                    )
                    .WaitAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (CreateTopicsException e) when (_OnlyAlreadyExists(e)) { }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                var logArgs = new LogMessageEventArgs
                {
                    LogType = MqLogType.ConsumeError,
                    Reason = "An error was encountered when automatically creating topic! -->" + e,
                };
                OnLogCallback!(logArgs);
            }
        }

        return normalizedTopics;
    }

    public ValueTask SubscribeAsync(IEnumerable<string> topics, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNull(topics);
        cancellationToken.ThrowIfCancellationRequested();

        Connect();

        // ReSharper disable once InconsistentlySynchronizedField -- volatile read after Connect guarantees the instance is published.
        _consumerClient!.Subscribe(topics);

        return ValueTask.CompletedTask;
    }

    public async ValueTask ListeningAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        Connect();
        var readyReported = false;

        while (!cancellationToken.IsCancellationRequested)
        {
            await _pauseGate.WaitIfPausedAsync(cancellationToken).ConfigureAwait(false);

            ConsumeResult<string, byte[]> consumerResult;

            try
            {
                lock (_consumeLock)
                {
                    var consumerClient = _consumerClient;
                    if (consumerClient is null)
                    {
                        return;
                    }

                    consumerResult = consumerClient.Consume(timeout);
                }

                if (Volatile.Read(ref _disposed) != 0)
                {
                    // Shutdown started while Consume was blocked: leave the record unstored so the next owner of
                    // the partition receives it.
                    return;
                }

                if (!readyReported)
                {
                    readyReported = true;
                    _ready.TrySetResult();
                }

                if (consumerResult == null)
                {
                    continue;
                }

                if (consumerResult.IsPartitionEOF)
                {
                    // The marker is not dispatched to a handler, so its offset never reaches
                    // CommitAsync. The concurrent-mode watermark still has to account for it or it
                    // stops at the first one and the partition is never committed again.
                    _ObserveUndispatched(consumerResult);

                    continue;
                }

                if (_pauseGate.IsPaused)
                {
                    // PauseAsync does not wait for a Consume already in progress, so the record it returned can
                    // arrive after the pause. Rewind to it instead of dispatching it; the poll resumes there.
                    _RewindUndispatched(consumerResult);

                    continue;
                }

                if (consumerResult.Message.Value == null)
                {
                    OnLogCallback?.Invoke(
                        new LogMessageEventArgs
                        {
                            LogType = MqLogType.ConsumeError,
                            Reason = "Kafka record had a null transport value and was terminally committed.",
                        }
                    );
                    _ObserveUndispatched(consumerResult);
                    continue;
                }

                var delivery = _TrackDelivery(consumerResult);

                if (_semaphore is not null)
                {
                    try
                    {
                        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception e) when (e is ObjectDisposedException or OperationCanceledException)
                    {
                        // Shutdown raced the concurrency-gate wait: stop cleanly. The offset for this
                        // delivery is never committed, so Kafka redelivers it to a replacement
                        // consumer. Mirrors the ObjectDisposedException guard already on
                        // _semaphore.Release() in _ReleaseSemaphore.
                        OnLogCallback?.Invoke(
                            new LogMessageEventArgs
                            {
                                LogType = MqLogType.ConsumeError,
                                Reason = $"Consumer stopped during shutdown before dispatch: {e.Message}",
                            }
                        );

                        return;
                    }

                    var handlerTask = Task.Run(
                        async () =>
                        {
                            try
                            {
                                await _ConsumeAsync(delivery).ConfigureAwait(false);
                            }
                            finally
                            {
                                _ReleaseSemaphore();
                            }
                        },
                        CancellationToken.None
                    );

                    _inFlightHandlers.Track(handlerTask, consumerResult.TopicPartition);
                    _ObserveBackgroundHandler(handlerTask);

                    continue;
                }

                var inlineTask = _ConsumeAsync(delivery);
                _inFlightHandlers.Track(inlineTask, consumerResult.TopicPartition);
                await inlineTask.ConfigureAwait(false);
            }
            catch (ConsumeException e) when (_kafkaOptions.RetriableErrorCodes.Contains((int)e.Error.Code))
            {
                var logArgs = new LogMessageEventArgs
                {
                    LogType = MqLogType.ConsumeRetries,
                    Reason = e.Error.ToString(),
                };
                OnLogCallback!(logArgs);

                continue;
            }
        }
        // ReSharper disable once FunctionNeverReturns
    }

    public ValueTask WaitUntilReadyAsync(CancellationToken cancellationToken = default)
    {
        return new ValueTask(_ready.Task.WaitAsync(cancellationToken));
    }

    public ValueTask CommitAsync(object? sender, CancellationToken cancellationToken = default)
    {
        // ReSharper disable once InconsistentlySynchronizedField -- volatile read; null-check fast path.
        if (!_TryGetDelivery(sender, out var delivery))
        {
            return ValueTask.CompletedTask;
        }

        lock (_lock)
        {
            var consumerClient = _consumerClient;

            if (consumerClient is null)
            {
                return ValueTask.CompletedTask;
            }

            if (!_OwnsPartition(delivery.ConsumerResult.TopicPartition))
            {
                return ValueTask.CompletedTask;
            }

            // Only stores the offset: the background auto-commit sends it to the broker, and a revoke or shutdown
            // commits it synchronously, so a handler never waits on a commit round trip.
            if (_offsetCommitTracker is null)
            {
                var consumerResult = delivery.ConsumerResult;
                _StoreOffsets(
                    consumerClient,
                    [new TopicPartitionOffset(consumerResult.TopicPartition, consumerResult.Offset + 1)]
                );

                return ValueTask.CompletedTask;
            }

            if (delivery.IsTracked)
            {
                _StoreOffsets(consumerClient, _offsetCommitTracker.MarkCommitted(delivery));
            }
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask RejectAsync(object? sender, CancellationToken cancellationToken = default)
    {
        // ReSharper disable once InconsistentlySynchronizedField -- volatile read; null-check fast path.
        if (!_TryGetDelivery(sender, out var delivery))
        {
            return ValueTask.CompletedTask;
        }

        lock (_lock)
        {
            var consumerClient = _consumerClient;

            if (consumerClient is null)
            {
                return ValueTask.CompletedTask;
            }

            if (!_OwnsPartition(delivery.ConsumerResult.TopicPartition))
            {
                return ValueTask.CompletedTask;
            }

            if (_offsetCommitTracker is not null && delivery.IsTracked)
            {
                if (!_offsetCommitTracker.MarkRejected(delivery))
                {
                    return ValueTask.CompletedTask;
                }
            }

            consumerClient.Seek(delivery.ConsumerResult.TopicPartitionOffset);
        }

        return ValueTask.CompletedTask;
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

        lock (_lock)
        {
            _consumerClient?.Pause(_consumerClient.Assignment);
        }
    }

    public async ValueTask ResumeAsync(CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        if (!await _pauseGate.ResumeAsync().ConfigureAwait(false))
        {
            return;
        }

        lock (_lock)
        {
            _consumerClient?.Resume(_consumerClient.Assignment);
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

        // Drain in-flight handlers while the consumer is still open, so each one can store its offset and the final
        // commit below includes it. Bounded so a stuck handler cannot block shutdown; one still running past the
        // budget never stores its offset, and Kafka redelivers the record (at-least-once).
        try
        {
            await _inFlightHandlers.DrainAsync(timeout, TimeProvider.System).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Handler faults are already surfaced by _ObserveBackgroundHandler; on a drain timeout or fault,
            // log and proceed, because disposal must never block or throw.
            OnLogCallback?.Invoke(
                new LogMessageEventArgs
                {
                    LogType = MqLogType.ExceptionReceived,
                    Reason = $"Timed out or faulted draining in-flight Kafka handlers during shutdown: {ex}",
                }
            );
        }

        IConsumer<string, byte[]>? consumerClient;
        lock (_lock)
        {
            consumerClient = _consumerClient;
            _consumerClient = null;
        }

        if (consumerClient is not null)
        {
            // Waits for the poll loop to leave Consume, which librdkafka does not allow to overlap Close.
            lock (_consumeLock)
            {
                try
                {
                    // With auto-commit on, Close commits the stored offsets synchronously before it leaves the group,
                    // so the drained handlers' final watermark reaches the broker without a second commit round trip.
                    consumerClient.Close();
                }
                catch (Exception e)
                {
                    // Dispose still releases native resources. Records past the last successful commit are
                    // redelivered to the next owner (at-least-once).
                    OnLogCallback?.Invoke(
                        new LogMessageEventArgs
                        {
                            LogType = MqLogType.ConsumeError,
                            Reason = $"Kafka consumer close failed; uncommitted records will be redelivered: {e}",
                        }
                    );
                }

                consumerClient.Dispose();
            }
        }

        _semaphore?.Dispose();
    }

    public void Connect()
    {
        // ReSharper disable once InconsistentlySynchronizedField -- double-checked locking; field is volatile.
        if (_consumerClient != null)
        {
            return;
        }

        lock (_lock)
        {
            // A disposed client must not reopen a consumer that shutdown would never close.
            if (_consumerClient == null && Volatile.Read(ref _disposed) == 0)
            {
                _consumerClient = _consumerFactory(BuildConfig(_kafkaOptions, _groupId, _consumerConfig));
            }
        }
    }

    /// <summary>Builds the consumer configuration for the consumer group <paramref name="groupId"/>.</summary>
    internal static ConsumerConfig BuildConfig(
        KafkaMessagingOptions options,
        string groupId,
        KafkaConsumerConfig? consumerConfig
    )
    {
        var config = new ConsumerConfig(new Dictionary<string, string>(options.MainConfig, StringComparer.Ordinal));
        config.BootstrapServers ??= options.Servers;
        config.GroupId ??= groupId;
        config.AutoOffsetReset ??= AutoOffsetReset.Earliest;
        config.IsolationLevel ??= consumerConfig?.IsolationLevel;
        config.AllowAutoCreateTopics ??= true;
        config.LogConnectionClose ??= false;

        // Incremental rebalancing: a member joining or leaving moves only the partitions that change owner, instead
        // of revoking every partition from every member and stopping the whole group while it reassigns them.
        config.PartitionAssignmentStrategy ??= PartitionAssignmentStrategy.CooperativeSticky;

        // The transport owns offset commits, so MainConfig cannot override these. An offset is stored only after its
        // handler settles (at-least-once) and the background auto-commit sends the stored offsets to the broker; a
        // revoke and shutdown commit them synchronously.
        config.EnableAutoOffsetStore = false;
        config.EnableAutoCommit = true;

        return config;
    }

    private KafkaDelivery _TrackDelivery(ConsumeResult<string, byte[]> consumerResult)
    {
        if (_offsetCommitTracker is null)
        {
            return new KafkaDelivery(consumerResult, KafkaDelivery.UntrackedGeneration);
        }

        lock (_lock)
        {
            return _offsetCommitTracker.Track(consumerResult);
        }
    }

    private void _ObserveUndispatched(ConsumeResult<string, byte[]> consumerResult)
    {
        lock (_lock)
        {
            var consumerClient = _consumerClient;

            if (consumerClient is null || !_OwnsPartition(consumerResult.TopicPartition))
            {
                return;
            }

            List<TopicPartitionOffset> committableOffsets;

            if (_offsetCommitTracker is null)
            {
                var offset = consumerResult.TopicPartitionOffset.Offset.Value;

                if (offset < 0)
                {
                    return;
                }

                var nextOffset = consumerResult.IsPartitionEOF ? offset : offset + 1;
                committableOffsets = [new TopicPartitionOffset(consumerResult.TopicPartition, new Offset(nextOffset))];
            }
            else
            {
                committableOffsets = _offsetCommitTracker.MarkObserved(consumerResult);
            }

            _StoreOffsets(consumerClient, committableOffsets);
        }
    }

    private void _RewindUndispatched(ConsumeResult<string, byte[]> consumerResult)
    {
        lock (_lock)
        {
            var consumerClient = _consumerClient;

            if (consumerClient is null || !_OwnsPartition(consumerResult.TopicPartition))
            {
                return;
            }

            consumerClient.Seek(consumerResult.TopicPartitionOffset);
        }
    }

    private static void _StoreOffsets(IConsumer<string, byte[]> consumerClient, List<TopicPartitionOffset> offsets)
    {
        foreach (var offset in offsets)
        {
            try
            {
                consumerClient.StoreOffset(offset);
            }
            catch (KafkaException e) when (e.Error.Code is ErrorCode.Local_State)
            {
                // librdkafka refuses to store an offset for a partition this member no longer holds. The new owner
                // reads from the last committed offset and redelivers the record (at-least-once).
            }
        }
    }

    private void _CommitStoredOffsets(IConsumer<string, byte[]> consumerClient)
    {
        try
        {
            consumerClient.Commit();
        }
        catch (KafkaException e) when (e.Error.Code is ErrorCode.Local_NoOffset)
        {
            // Nothing stored since the last commit.
        }
        catch (KafkaException e)
        {
            // The records past the last successful commit are redelivered to the next owner (at-least-once).
            OnLogCallback?.Invoke(
                new LogMessageEventArgs
                {
                    LogType = MqLogType.ConsumeError,
                    Reason = $"Kafka offset commit failed; uncommitted records will be redelivered: {e.Error}",
                }
            );
        }
    }

    private static bool _OnlyAlreadyExists(CreateTopicsException exception)
    {
        return exception.Results.TrueForAll(static result =>
            result.Error.Code is ErrorCode.NoError or ErrorCode.TopicAlreadyExists
        );
    }

    private async Task _ConsumeAsync(KafkaDelivery delivery)
    {
        var consumerResult = delivery.ConsumerResult;
        Dictionary<string, string?> headers;
        try
        {
            headers = new Dictionary<string, string?>(consumerResult.Message.Headers.Count, StringComparer.Ordinal);
            foreach (var header in consumerResult.Message.Headers)
            {
                var val = header.GetValueBytes();
                headers[header.Key] = val != null ? Encoding.UTF8.GetString(val) : null;
            }
        }
        catch (Exception ex)
        {
            await _TerminallyCommitMalformedEnvelopeAsync(delivery, ex).ConfigureAwait(false);
            return;
        }

        if (_kafkaOptions.CustomHeadersBuilder != null)
        {
            try
            {
                var customHeaders = _kafkaOptions.CustomHeadersBuilder(consumerResult, _serviceProvider);
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
                        Reason = $"Kafka custom headers builder failed; seeking offset for retry: {ex.GetType().Name}",
                    }
                );

                await RejectAsync(delivery).ConfigureAwait(false);
                return;
            }
        }

        // Stamped after the custom headers builder so neither the wire nor the builder can choose the address.
        headers[Headers.TransportAddress] = consumerResult.Topic;

        TransportMessage message;
        try
        {
            _ValidateRequiredHeaders(headers);
            message = new TransportMessage(headers, consumerResult.Message.Value);
        }
        catch (Exception ex)
        {
            await _TerminallyCommitMalformedEnvelopeAsync(delivery, ex).ConfigureAwait(false);
            return;
        }

        await OnMessageCallback!(message, delivery).ConfigureAwait(false);
    }

    private async Task _TerminallyCommitMalformedEnvelopeAsync(KafkaDelivery delivery, Exception exception)
    {
        OnLogCallback?.Invoke(
            new LogMessageEventArgs
            {
                LogType = MqLogType.ConsumeError,
                Reason =
                    $"Failed to build transport message; the Kafka offset was terminally committed: {exception.GetType().Name}",
            }
        );

        await CommitAsync(delivery).ConfigureAwait(false);
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
            throw new InvalidDataException("The Kafka transport envelope is missing a required Messaging header.");
        }
    }

    private IConsumer<string, byte[]> _BuildConsumer(ConsumerConfig config)
    {
        return new ConsumerBuilder<string, byte[]>(config)
            .SetErrorHandler(_ConsumerClientOnConsumeError)
            .SetPartitionsAssignedHandler((_, partitions) => PartitionsAssigned(partitions))
            .SetPartitionsRevokedHandler((_, partitions) => PartitionsRevoked(partitions))
            .SetPartitionsLostHandler((_, partitions) => PartitionsLost(partitions))
            .Build();
    }

    internal void PartitionsAssigned(IEnumerable<TopicPartition> partitions)
    {
        lock (_lock)
        {
            _hasPartitionAssignment = true;

            foreach (var partition in partitions)
            {
                _ownedPartitions.Add(partition);
                _offsetCommitTracker?.Reset(partition);
            }
        }
    }

    internal void PartitionsRevoked(IEnumerable<TopicPartitionOffset> partitions)
    {
        var revoked = partitions.Select(x => x.TopicPartition).ToHashSet();

        // A revoke during shutdown comes from Close, after the drain; Close itself commits the stored offsets.
        if (Volatile.Read(ref _disposed) == 0)
        {
            _DrainRevokedHandlers(revoked);

            IConsumer<string, byte[]>? consumerClient;
            lock (_lock)
            {
                consumerClient = _consumerClient;
            }

            // The partitions are still assigned until this callback returns, so their final watermark, stored by
            // the handlers that just finished, can still be committed before the next owner starts reading.
            if (consumerClient is not null)
            {
                _CommitStoredOffsets(consumerClient);
            }
        }

        _PartitionsRemoved(revoked);
    }

    private void _DrainRevokedHandlers(HashSet<TopicPartition> revoked)
    {
        var affected = _inFlightHandlers.Snapshot(tag =>
            tag is TopicPartition partition && revoked.Contains(partition)
        );

        if (affected.Length == 0)
        {
            return;
        }

        try
        {
            // Rebalance callbacks are synchronous and run on the poll thread, so the wait has to block. The handlers
            // never need the poll thread: they store offsets under _lock, which this thread does not hold.
#pragma warning disable VSTHRD002, CA1849 // A rebalance callback is synchronous; blocking the poll thread is the point.
            if (!Task.WaitAll(affected, _RevokeDrainTimeout))
#pragma warning restore VSTHRD002, CA1849
            {
                OnLogCallback?.Invoke(
                    new LogMessageEventArgs
                    {
                        LogType = MqLogType.ConsumeError,
                        Reason =
                            $"Kafka handlers on revoked partitions were still running after {_RevokeDrainTimeout}; their records will be redelivered to the new owner.",
                    }
                );
            }
        }
#pragma warning disable ERP022 // Handler faults are already surfaced by _ObserveBackgroundHandler.
        catch (AggregateException) { }
#pragma warning restore ERP022
    }

    internal void PartitionsLost(IEnumerable<TopicPartitionOffset> partitions)
    {
        _PartitionsRemoved(partitions.Select(x => x.TopicPartition));
    }

    private void _PartitionsRemoved(IEnumerable<TopicPartition> partitions)
    {
        lock (_lock)
        {
            _hasPartitionAssignment = true;

            foreach (var partition in partitions)
            {
                _ownedPartitions.Remove(partition);
                _offsetCommitTracker?.Reset(partition);
            }
        }
    }

    private bool _OwnsPartition(TopicPartition partition)
    {
        return !_hasPartitionAssignment || _ownedPartitions.Contains(partition);
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
                        new LogMessageEventArgs { LogType = MqLogType.ConsumeError, Reason = exception.Message }
                    );
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default
        );
    }

    private static IAdminClient _BuildAdminClient(AdminClientConfig config)
    {
        return new AdminClientBuilder(config).Build();
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
            // Defensive: ignore over-release
        }
        catch (ObjectDisposedException)
        {
            // Shutdown in progress — semaphore already disposed
        }
    }

    private void _ConsumerClientOnConsumeError(IConsumer<string, byte[]> consumer, Error e)
    {
        var logArgs = new LogMessageEventArgs
        {
            LogType = MqLogType.ServerConnError,
            Reason = $"An error occurred during connect kafka --> {e.Reason}",
        };
        OnLogCallback!(logArgs);
    }

    private static bool _TryGetDelivery(object? sender, out KafkaDelivery delivery)
    {
        switch (sender)
        {
            case KafkaDelivery tracked:
                delivery = tracked;

                return true;

            case ConsumeResult<string, byte[]> consumerResult:
                delivery = new KafkaDelivery(consumerResult, KafkaDelivery.UntrackedGeneration);

                return true;

            default:
                delivery = null!;

                return false;
        }
    }

    internal sealed class KafkaDelivery(ConsumeResult<string, byte[]> consumerResult, long generation)
    {
        public const long UntrackedGeneration = -1;

        public ConsumeResult<string, byte[]> ConsumerResult { get; } = consumerResult;

        public long Generation { get; } = generation;

        public bool IsTracked => Generation != UntrackedGeneration;
    }

    /// <summary>
    /// Tracks which offsets are still being handled per partition so concurrent handlers can only ever
    /// commit a watermark that no in-flight delivery sits below.
    /// </summary>
    internal sealed class KafkaOffsetCommitTracker
    {
        private readonly Dictionary<TopicPartition, PartitionCommitState> _partitions = [];

        public KafkaDelivery Track(ConsumeResult<string, byte[]> consumerResult)
        {
            var offset = consumerResult.TopicPartitionOffset.Offset.Value;

            if (offset < 0)
            {
                return new KafkaDelivery(consumerResult, KafkaDelivery.UntrackedGeneration);
            }

            var state = _GetOrAddState(consumerResult.TopicPartition);

            if (state.NextCommitOffset is null || offset < state.NextCommitOffset.Value)
            {
                state.NextCommitOffset = offset;
            }

            state.PendingOffsets.Add(offset);
            _Observe(state, offset, offset + 1);

            return new KafkaDelivery(consumerResult, state.Generation);
        }

        /// <summary>
        /// Records an offset the poll loop saw but never dispatched — a tombstone, or an end-of-partition
        /// marker — and returns the highest commit offset that does not pass an in-flight delivery.
        /// </summary>
        public List<TopicPartitionOffset> MarkObserved(ConsumeResult<string, byte[]> consumerResult)
        {
            var offset = consumerResult.TopicPartitionOffset.Offset.Value;

            if (offset < 0)
            {
                return [];
            }

            var state = _GetOrAddState(consumerResult.TopicPartition);
            var isInitialObservation = state.NextCommitOffset is null;
            state.NextCommitOffset ??= offset;

            // An end-of-partition result already carries the log-end offset (the next offset that will be
            // produced there), whereas a record carries its own offset.
            var nextOffset = consumerResult.IsPartitionEOF ? offset : offset + 1;

            if (!_Observe(state, offset, nextOffset))
            {
                return [];
            }

            var candidate =
                state.PendingOffsets.Count > 0
                    ? Math.Min(state.PendingOffsets.Min, state.NextObservedOffset)
                    : state.NextObservedOffset;
            var nextCommitOffset = state.NextCommitOffset.Value;

            // The first EOF observation is itself a useful commit even though its offset is already the
            // next offset to read. Subsequent observations at the same frontier need no duplicate commit.
            if (candidate < nextCommitOffset || (candidate == nextCommitOffset && !isInitialObservation))
            {
                return [];
            }

            state.NextCommitOffset = candidate;

            return [new TopicPartitionOffset(consumerResult.TopicPartition, new Offset(candidate))];
        }

        public void Reset(TopicPartition partition)
        {
            if (!_partitions.TryGetValue(partition, out var state))
            {
                return;
            }

            state.Generation++;
            state.NextCommitOffset = null;
            state.NextObservedOffset = 0;
            state.IsReplaying = false;
            state.PendingOffsets.Clear();
        }

        public List<TopicPartitionOffset> MarkCommitted(KafkaDelivery delivery)
        {
            var consumerResult = delivery.ConsumerResult;
            var offset = consumerResult.TopicPartitionOffset.Offset.Value;

            if (
                offset < 0
                || !_partitions.TryGetValue(consumerResult.TopicPartition, out var state)
                || state.Generation != delivery.Generation
                || state.NextCommitOffset is not { } nextCommitOffset
            )
            {
                return [];
            }

            state.PendingOffsets.Remove(offset);

            // Everything below the lowest still-in-flight offset is done. With nothing in flight the
            // watermark moves up to everything the poll loop has seen, which is what carries it over the
            // offsets the broker never delivers — transaction control records, aborted batches under
            // read_committed, and compaction holes — instead of stalling on them forever.
            var candidate =
                state.PendingOffsets.Count > 0
                    ? Math.Min(state.PendingOffsets.Min, state.NextObservedOffset)
                    : state.NextObservedOffset;

            if (candidate <= nextCommitOffset)
            {
                return [];
            }

            state.NextCommitOffset = candidate;

            return [new TopicPartitionOffset(consumerResult.TopicPartition, new Offset(candidate))];
        }

        public bool MarkRejected(KafkaDelivery delivery)
        {
            var consumerResult = delivery.ConsumerResult;

            if (
                !_partitions.TryGetValue(consumerResult.TopicPartition, out var state)
                || state.Generation != delivery.Generation
            )
            {
                return false;
            }

            var offset = consumerResult.TopicPartitionOffset.Offset.Value;
            state.Generation++;
            state.PendingOffsets.Clear();

            if (offset < 0)
            {
                state.NextCommitOffset = null;
                state.NextObservedOffset = 0;
                state.IsReplaying = false;

                return true;
            }

            // The caller seeks back to this offset, so the range above it is about to be replayed and
            // must not count as seen until the replay actually delivers it.
            state.NextCommitOffset = offset;
            state.NextObservedOffset = offset;
            state.IsReplaying = true;

            return true;
        }

        private PartitionCommitState _GetOrAddState(TopicPartition topicPartition)
        {
            if (_partitions.TryGetValue(topicPartition, out var state))
            {
                return state;
            }

            state = new PartitionCommitState();
            _partitions.Add(topicPartition, state);

            return state;
        }

        private static bool _Observe(PartitionCommitState state, long offset, long nextOffset)
        {
            if (state.IsReplaying)
            {
                if (offset > state.NextObservedOffset)
                {
                    // A delivery the poll loop already held when the reject seeked back. The replay will
                    // hand out the whole range again, so it is not proof that the gap below is finished.
                    return false;
                }

                state.IsReplaying = false;
            }

            state.NextObservedOffset = Math.Max(state.NextObservedOffset, nextOffset);

            return true;
        }

        private sealed class PartitionCommitState
        {
            public long Generation { get; set; }

            /// <summary>The offset to commit, meaning the next offset this consumer would read.</summary>
            public long? NextCommitOffset { get; set; }

            /// <summary>
            /// The offset the poll loop expects next: one past the highest delivery it has seen. Deliveries
            /// are ordered within a partition, so nothing below this can still be on its way.
            /// </summary>
            public long NextObservedOffset { get; set; }

            /// <summary>Whether a reject seeked back and the replay has not reached that offset yet.</summary>
            public bool IsReplaying { get; set; }

            /// <summary>Tracked deliveries that have not been committed yet, bounded by the group concurrency.</summary>
            public SortedSet<long> PendingOffsets { get; } = [];
        }
    }
}
