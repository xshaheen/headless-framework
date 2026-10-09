// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using Headless.Checks;
using Headless.Messaging.AzureServiceBus.Helpers;
using Headless.Messaging.Transport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.AzureServiceBus;

internal sealed class AzureServiceBusConsumerClient(
    ILogger logger,
    string subscriptionName,
    byte groupConcurrent,
    IOptions<AzureServiceBusMessagingOptions> options,
    IServiceProvider serviceProvider,
    IAzureServiceBusClientPool clientPool,
    MessageLane lane = MessageLane.Bus,
    ConsumerSubscriptionKind kind = ConsumerSubscriptionKind.Competing
) : IConsumerClient
{
    // Headless must settle only after durable receive storage and handler outcome are known.
    private const bool _AutoCompleteMessages = false;

    // A malformed envelope never reaches the core, so it has no poison record: the dead-letter subqueue is the only
    // place it can still be inspected.
    private const string _MalformedEnvelopeReason = "MalformedEnvelope";

    /// <summary>
    /// How long Azure keeps an every-instance subscription nobody receives from: the shortest idle period Azure allows,
    /// and so the longest a crashed process leaves its subscription behind.
    /// </summary>
    internal static readonly TimeSpan EveryInstanceAutoDeleteOnIdle = TimeSpan.FromMinutes(5);

    private readonly bool _everyInstance = kind is ConsumerSubscriptionKind.EveryInstance;
    private int _recoveringSubscription;
    private IReadOnlyList<string> _subscribedMessageNames = [];
    private Func<CancellationToken, Task>? _onReestablished;

    private readonly AzureServiceBusMessagingOptions _asbOptions = Argument.IsNotNull(options.Value);
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private readonly SemaphoreSlim _semaphore = new(groupConcurrent);
    private readonly ConsumerPauseGate _pauseGate = new();
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private int _disposed;
    private int _hasStartedProcessing;
    private ServiceBusAdministrationClient? _administrationClient;

#pragma warning disable CA2213 // Justification: shared client owned and disposed by AzureServiceBusClientPool
    private ServiceBusClient? _serviceBusClient;
#pragma warning restore CA2213
    private ServiceBusProcessorFacade? _serviceBusProcessor;
    private readonly List<ServiceBusProcessorFacade> _queueProcessors = [];

    public Func<TransportMessage, object?, Task>? OnMessageCallback { get; set; }

    public Action<LogMessageEventArgs>? OnLogCallback { get; set; }

    public void AttachCallbacks(Func<TransportMessage, object?, Task>? onMessage, Action<LogMessageEventArgs>? onLog)
    {
        OnMessageCallback = onMessage;
        OnLogCallback = onLog;
    }

    public void AttachReestablishedCallback(Func<CancellationToken, Task>? onReestablished)
    {
        Volatile.Write(ref _onReestablished, onReestablished);
    }

    public BrokerAddress BrokerAddress =>
        ServiceBusHelpers.GetBrokerAddress(_asbOptions.ConnectionString, _asbOptions.Namespace);

    public async ValueTask SubscribeAsync(
        IEnumerable<string> messageNames,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(messageNames);

        await ConnectAsync(cancellationToken).ConfigureAwait(false);

        if (lane == MessageLane.Queue)
        {
            foreach (var messageName in messageNames)
            {
                CheckValidQueueName(messageName);
                await _EnsureQueueProcessorAsync(messageName, cancellationToken).ConfigureAwait(false);
            }

            return;
        }

        if (!_asbOptions.AutoProvision)
        {
            return;
        }

        var ruleNames = messageNames
            .Concat(_asbOptions.SqlFilters.Select(o => o.Key))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (!_everyInstance)
        {
            await _SyncRulesAsync(ruleNames, cancellationToken).ConfigureAwait(false);
            return;
        }

        _subscribedMessageNames = ruleNames;
        await _ProvisionEveryInstanceSubscriptionAsync(ruleNames, cancellationToken).ConfigureAwait(false);
    }

    // An every-instance subscription belongs to this process, so it is created on first subscribe rather than when the
    // client connects: the topology-only client the core creates and disposes never subscribes and leaves nothing.
    // Returns whether this call created it, which only then means messages published meanwhile were lost.
    private async Task<bool> _ProvisionEveryInstanceSubscriptionAsync(
        IReadOnlyList<string> ruleNames,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var created = false;

            if (
                !await _administrationClient!
                    .SubscriptionExistsAsync(_asbOptions.TopicPath, subscriptionName, cancellationToken)
                    .ConfigureAwait(false)
            )
            {
                var subscription = new CreateSubscriptionOptions(_asbOptions.TopicPath, subscriptionName)
                {
                    RequiresSession = _asbOptions.EnableSessions,
                    AutoDeleteOnIdle = EveryInstanceAutoDeleteOnIdle,
                    LockDuration = _asbOptions.SubscriptionMessageLockDuration,
                    DefaultMessageTimeToLive = _asbOptions.SubscriptionDefaultMessageTimeToLive,
                    MaxDeliveryCount = _asbOptions.SubscriptionMaxDeliveryCount,
                };

                // Created with its first rule instead of the default match-all rule, so the subscription never
                // receives messages this process does not consume.
                if (ruleNames.Count == 0)
                {
                    await _administrationClient
                        .CreateSubscriptionAsync(subscription, cancellationToken)
                        .ConfigureAwait(false);
                }
                else
                {
                    await _administrationClient
                        .CreateSubscriptionAsync(
                            subscription,
                            new CreateRuleOptions { Name = ruleNames[0], Filter = _CreateRuleFilter(ruleNames[0]) },
                            cancellationToken
                        )
                        .ConfigureAwait(false);
                }

                logger.SubscriptionCreated(_asbOptions.TopicPath, subscriptionName);
                created = true;
            }

            await _SyncRulesAsync(ruleNames, cancellationToken).ConfigureAwait(false);

            return created;
        }
        catch (UnauthorizedAccessException e)
        {
            throw new InvalidOperationException(
                $"Azure Service Bus could not create the every-instance subscription '{subscriptionName}' on topic "
                    + $"'{_asbOptions.TopicPath}': every-instance consumers need Manage rights on the namespace, because "
                    + "each process creates and deletes a subscription of its own.",
                e
            );
        }
    }

    private async Task _SyncRulesAsync(IReadOnlyCollection<string> ruleNames, CancellationToken cancellationToken)
    {
        var allRuleNames = new List<string>();

        await foreach (
#pragma warning disable MA0079 // False positive
            var rule in _administrationClient!
                .GetRulesAsync(_asbOptions.TopicPath, subscriptionName, cancellationToken)
                .ConfigureAwait(false)
#pragma warning restore MA0079
        )
        {
            allRuleNames.Add(rule.Name);
        }

        foreach (var newRule in ruleNames.Except(allRuleNames, StringComparer.Ordinal))
        {
            await _administrationClient
                .CreateRuleAsync(
                    _asbOptions.TopicPath,
                    subscriptionName,
                    new CreateRuleOptions { Name = newRule, Filter = _CreateRuleFilter(newRule) },
                    cancellationToken
                )
                .ConfigureAwait(false);

            logger.RuleAdded(newRule);
        }

        foreach (var oldRule in allRuleNames.Except(ruleNames, StringComparer.Ordinal))
        {
            await _administrationClient
                .DeleteRuleAsync(_asbOptions.TopicPath, subscriptionName, oldRule, cancellationToken)
                .ConfigureAwait(false);

            logger.RuleRemoved(oldRule);
        }
    }

    private RuleFilter _CreateRuleFilter(string ruleName)
    {
        var sqlExpression = _asbOptions
            .SqlFilters.FirstOrDefault(o => string.Equals(o.Key, ruleName, StringComparison.Ordinal))
            .Value;

        if (sqlExpression is not null)
        {
            return new SqlRuleFilter(sqlExpression);
        }

        var correlationRule = new CorrelationRuleFilter { Subject = ruleName };

        foreach (var correlationHeader in _asbOptions.DefaultCorrelationHeaders)
        {
            correlationRule.ApplicationProperties.Add(correlationHeader.Key, correlationHeader.Value);
        }

        return correlationRule;
    }

    public async ValueTask ListeningAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        await ConnectAsync(cancellationToken).ConfigureAwait(false);

        IReadOnlyList<ServiceBusProcessorFacade> processors =
            lane == MessageLane.Queue ? _queueProcessors : [_serviceBusProcessor!];

        foreach (var processor in processors)
        {
            if (processor.IsSessionProcessor)
            {
                processor.ProcessSessionMessageAsync += _ServiceBusProcessor_ProcessSessionMessageAsync;
            }
            else
            {
                processor.ProcessMessageAsync += _ServiceBusProcessor_ProcessMessageAsync;
            }

            processor.ProcessErrorAsync += _ServiceBusProcessor_ProcessErrorAsync;
        }

        await _pauseGate.WaitIfPausedAsync(cancellationToken).ConfigureAwait(false);

        foreach (var processor in processors)
        {
            await processor.StartProcessingAsync(cancellationToken).ConfigureAwait(false);
        }

        Volatile.Write(ref _hasStartedProcessing, 1);
        _ready.TrySetResult();
    }

    public ValueTask WaitUntilReadyAsync(CancellationToken cancellationToken = default)
    {
        return new ValueTask(_ready.Task.WaitAsync(cancellationToken));
    }

    public async ValueTask CommitAsync(object? sender, CancellationToken cancellationToken = default)
    {
        var commitInput = (AzureServiceBusConsumerCommitInput)sender!;
        await commitInput.CompleteMessageAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask RejectAsync(object? sender, CancellationToken cancellationToken = default)
    {
        var commitInput = (AzureServiceBusConsumerCommitInput)sender!;
        await commitInput.AbandonMessageAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DeadLetterAsync(
        object? sender,
        string reason,
        string? description,
        CancellationToken cancellationToken = default
    )
    {
        var commitInput = (AzureServiceBusConsumerCommitInput)sender!;
        await commitInput.DeadLetterMessageAsync(reason, description, cancellationToken).ConfigureAwait(false);
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

        foreach (var processor in _GetProcessors())
        {
            await processor.StopProcessingAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async ValueTask ResumeAsync(CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        // ASB is push-based — release the gate first (only affects startup gating),
        // then restart the processor which delivers messages via callbacks.
        if (!await _pauseGate.ResumeAsync().ConfigureAwait(false))
        {
            return;
        }

        if (Volatile.Read(ref _hasStartedProcessing) == 0)
        {
            return;
        }

        foreach (var processor in _GetProcessors())
        {
            if (processor.IsProcessing)
            {
                continue;
            }

            await processor.StartProcessingAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _pauseGate.Release();
        _ready.TrySetCanceled();

        if (_serviceBusProcessor is not null)
        {
            await _serviceBusProcessor.DisposeAsync().ConfigureAwait(false);
        }

        foreach (var processor in _queueProcessors)
        {
            await processor.DisposeAsync().ConfigureAwait(false);
        }

        // The ServiceBusClient is shared and pool-owned; other consumers and the publish
        // transports keep using it after this consumer stops. Only processors are ours.

        _connectionLock.Dispose();
        _semaphore.Dispose();
    }

    private void _ReleaseSemaphore()
    {
        if (groupConcurrent > 0)
        {
            try
            {
                _semaphore.Release();
            }
            catch (SemaphoreFullException)
            {
                // Defensive: ignore over-release
            }
        }
    }

    private Task _ServiceBusProcessor_ProcessErrorAsync(ProcessErrorEventArgs args)
    {
        var exceptionMessage =
            $"- Identifier: {args.Identifier}"
            + Environment.NewLine
            + $"- Entity Path: {args.EntityPath}"
            + Environment.NewLine
            + $"- Executing ErrorSource: {args.ErrorSource}"
            + Environment.NewLine
            + $"- Exception: {args.Exception}";

        var logArgs = new LogMessageEventArgs { LogType = MqLogType.ExceptionReceived, Reason = exceptionMessage };

        OnLogCallback!(logArgs);

        return
            _everyInstance
            && args.Exception is ServiceBusException { Reason: ServiceBusFailureReason.MessagingEntityNotFound }
            ? _RecoverEveryInstanceSubscriptionAsync(args.CancellationToken)
            : Task.CompletedTask;
    }

    // Azure deletes an every-instance subscription nobody received from for the idle period, which a connection lost
    // for that long causes. The processor then keeps failing on the missing entity, so the client recreates the
    // subscription and reports it: whatever was published in between is gone.
    private async Task _RecoverEveryInstanceSubscriptionAsync(CancellationToken cancellationToken)
    {
        if (
            Volatile.Read(ref _disposed) != 0
            || _administrationClient is null
            || Interlocked.CompareExchange(ref _recoveringSubscription, 1, 0) != 0
        )
        {
            return;
        }

        try
        {
            var recreated = await _ProvisionEveryInstanceSubscriptionAsync(_subscribedMessageNames, cancellationToken)
                .ConfigureAwait(false);

            // A missing-entity error while the subscription still exists (a transient lookup failure, or a sibling
            // error already recovered it) lost nothing, so it must not make the consumer flush its state again.
            var onReestablished = Volatile.Read(ref _onReestablished);
            if (recreated && onReestablished is not null)
            {
                await onReestablished(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception e) when (e is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // The processor reports the next failure, which retries the recovery.
            OnLogCallback?.Invoke(
                new LogMessageEventArgs
                {
                    LogType = MqLogType.ExceptionReceived,
                    Reason = $"Azure Service Bus could not recreate subscription '{subscriptionName}': {e.Message}",
                }
            );
        }
        finally
        {
            Volatile.Write(ref _recoveringSubscription, 0);
        }
    }

    private async Task _ServiceBusProcessor_ProcessMessageAsync(ProcessMessageEventArgs arg)
    {
        Dictionary<string, string?> headers;
        try
        {
            headers = _ConvertHeaders(arg.Message);
        }
        catch (Exception exception)
        {
            _LogMalformedEnvelope(exception);
            await arg.DeadLetterMessageAsync(
                    arg.Message,
                    _MalformedEnvelopeReason,
                    exception.GetType().Name,
                    CancellationToken.None
                )
                .ConfigureAwait(false);
            return;
        }

        _ApplyCustomHeaders(arg.Message, headers);

        TransportMessage context;
        try
        {
            context = _CreateTransportMessage(arg.Message, arg.EntityPath, headers);
        }
        catch (Exception exception)
        {
            _LogMalformedEnvelope(exception);
            await arg.DeadLetterMessageAsync(
                    arg.Message,
                    _MalformedEnvelopeReason,
                    exception.GetType().Name,
                    CancellationToken.None
                )
                .ConfigureAwait(false);
            return;
        }

        await _DispatchAsync(context, new AzureServiceBusConsumerCommitInput(arg), arg.CancellationToken)
            .ConfigureAwait(false);
    }

    // A queue-lane client runs one processor per queue, so the semaphore keeps their sum within the consumer's
    // concurrency; with a single processor it never waits.
    private async Task _DispatchAsync(
        TransportMessage context,
        AzureServiceBusConsumerCommitInput commitInput,
        CancellationToken cancellationToken
    )
    {
        if (groupConcurrent == 0)
        {
            await OnMessageCallback!(context, commitInput).ConfigureAwait(false);
            return;
        }

        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await OnMessageCallback!(context, commitInput).ConfigureAwait(false);
        }
        finally
        {
            _ReleaseSemaphore();
        }
    }

    private async Task _ServiceBusProcessor_ProcessSessionMessageAsync(ProcessSessionMessageEventArgs arg)
    {
        Dictionary<string, string?> headers;
        try
        {
            headers = _ConvertHeaders(arg.Message);
        }
        catch (Exception exception)
        {
            _LogMalformedEnvelope(exception);
            await arg.DeadLetterMessageAsync(
                    arg.Message,
                    _MalformedEnvelopeReason,
                    exception.GetType().Name,
                    CancellationToken.None
                )
                .ConfigureAwait(false);
            return;
        }

        _ApplyCustomHeaders(arg.Message, headers);

        TransportMessage context;
        try
        {
            context = _CreateTransportMessage(arg.Message, arg.EntityPath, headers);
        }
        catch (Exception exception)
        {
            _LogMalformedEnvelope(exception);
            await arg.DeadLetterMessageAsync(
                    arg.Message,
                    _MalformedEnvelopeReason,
                    exception.GetType().Name,
                    CancellationToken.None
                )
                .ConfigureAwait(false);
            return;
        }

        await _DispatchAsync(context, new AzureServiceBusConsumerCommitInput(arg), arg.CancellationToken)
            .ConfigureAwait(false);
    }

    private void _LogMalformedEnvelope(Exception exception)
    {
        OnLogCallback?.Invoke(
            new LogMessageEventArgs
            {
                LogType = MqLogType.ConsumeError,
                Reason = $"Malformed Azure Service Bus transport envelope dead-lettered: {exception.GetType().Name}",
            }
        );
    }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (_serviceBusProcessor != null || (lane == MessageLane.Queue && _serviceBusClient != null))
        {
            return;
        }

        await _connectionLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (_serviceBusProcessor == null && (lane != MessageLane.Queue || _serviceBusClient == null))
            {
                // Shared, pool-owned resources: processors created from this client multiplex the
                // same AMQP connection as the publish senders. This client is never disposed here.
                _serviceBusClient = clientPool.GetClient();

                if (_asbOptions.AutoProvision)
                {
                    _administrationClient = clientPool.GetAdministrationClient();
                }

                if (lane == MessageLane.Bus && _asbOptions.AutoProvision)
                {
                    var administrationClient = _administrationClient!;
                    var topicConfigs = _asbOptions
                        .CustomProducers.Select(producer =>
                            (topicPaths: producer.TopicPath, subscribe: producer.CreateSubscription)
                        )
                        .Append((topicPaths: _asbOptions.TopicPath, subscribe: true))
                        .GroupBy(n => n.topicPaths, StringComparer.OrdinalIgnoreCase)
                        .Select(n => (topicPaths: n.Key, subscribe: n.Max(o => o.subscribe)));

                    foreach (var (topicPath, subscribe) in topicConfigs)
                    {
                        if (
                            !await administrationClient
                                .TopicExistsAsync(topicPath, cancellationToken)
                                .ConfigureAwait(false)
                        )
                        {
                            await administrationClient
                                .CreateTopicAsync(topicPath, cancellationToken)
                                .ConfigureAwait(false);
                            logger.TopicCreated(topicPath);
                        }

                        // An every-instance subscription is created by SubscribeAsync, and only on the topic the
                        // processor reads, so the per-process name never lands on a topic nothing receives from.
                        if (
                            subscribe
                            && !_everyInstance
                            && !await administrationClient
                                .SubscriptionExistsAsync(topicPath, subscriptionName, cancellationToken)
                                .ConfigureAwait(false)
                        )
                        {
                            var subscriptionDescription = new CreateSubscriptionOptions(topicPath, subscriptionName)
                            {
                                RequiresSession = _asbOptions.EnableSessions,
                                AutoDeleteOnIdle = _asbOptions.SubscriptionAutoDeleteOnIdle,
                                LockDuration = _asbOptions.SubscriptionMessageLockDuration,
                                DefaultMessageTimeToLive = _asbOptions.SubscriptionDefaultMessageTimeToLive,
                                MaxDeliveryCount = _asbOptions.SubscriptionMaxDeliveryCount,
                            };

                            await administrationClient
                                .CreateSubscriptionAsync(subscriptionDescription, cancellationToken)
                                .ConfigureAwait(false);

                            logger.SubscriptionCreated(topicPath, subscriptionName);
                        }
                    }
                }

                if (lane == MessageLane.Queue)
                {
                    return;
                }

                _serviceBusProcessor = _CreateProcessor(_asbOptions.TopicPath, subscriptionName);
            }
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    private async Task _EnsureQueueProcessorAsync(string queueName, CancellationToken cancellationToken)
    {
        await ConnectAsync(cancellationToken).ConfigureAwait(false);

        if (_asbOptions.AutoProvision && _administrationClient is not null)
        {
            if (!await _administrationClient.QueueExistsAsync(queueName, cancellationToken).ConfigureAwait(false))
            {
                var queueOptions = new CreateQueueOptions(queueName)
                {
                    RequiresSession = _asbOptions.EnableSessions,
                    AutoDeleteOnIdle = _asbOptions.SubscriptionAutoDeleteOnIdle,
                    LockDuration = _asbOptions.SubscriptionMessageLockDuration,
                    DefaultMessageTimeToLive = _asbOptions.SubscriptionDefaultMessageTimeToLive,
                    MaxDeliveryCount = _asbOptions.SubscriptionMaxDeliveryCount,
                };

                await _administrationClient.CreateQueueAsync(queueOptions, cancellationToken).ConfigureAwait(false);
            }
        }

        var processor = _CreateProcessor(queueName, topicSubscription: null);

        _queueProcessors.Add(processor);
    }

    // The consumer's concurrency sizes the processor itself: the semaphore in the handler only caps the sum across the
    // processors of one queue-lane client, so a processor capped below it would leave the consumer's concurrency unused.
    // A session processor takes one message at a time per session, which keeps each session in order, and spends the
    // concurrency on sessions instead.
    private ServiceBusProcessorFacade _CreateProcessor(string entityPath, string? topicSubscription)
    {
        var concurrency = Math.Max(1, (int)groupConcurrent);

        if (!_asbOptions.EnableSessions)
        {
            var processorOptions = new ServiceBusProcessorOptions
            {
                AutoCompleteMessages = _AutoCompleteMessages,
                MaxConcurrentCalls = concurrency,
                PrefetchCount = _asbOptions.PrefetchCount,
                MaxAutoLockRenewalDuration = _asbOptions.MaxAutoLockRenewalDuration,
            };

            return new ServiceBusProcessorFacade(
                serviceBusProcessor: topicSubscription is null
                    ? _serviceBusClient!.CreateProcessor(entityPath, processorOptions)
                    : _serviceBusClient!.CreateProcessor(entityPath, topicSubscription, processorOptions)
            );
        }

        var sessionOptions = new ServiceBusSessionProcessorOptions
        {
            AutoCompleteMessages = _AutoCompleteMessages,
            MaxConcurrentSessions = concurrency,
            MaxConcurrentCallsPerSession = 1,
            PrefetchCount = _asbOptions.PrefetchCount,
            MaxAutoLockRenewalDuration = _asbOptions.MaxAutoLockRenewalDuration,
            SessionIdleTimeout = _asbOptions.SessionIdleTimeout,
        };

        return new ServiceBusProcessorFacade(
            serviceBusSessionProcessor: topicSubscription is null
                ? _serviceBusClient!.CreateSessionProcessor(entityPath, sessionOptions)
                : _serviceBusClient!.CreateSessionProcessor(entityPath, topicSubscription, sessionOptions)
        );
    }

    private IEnumerable<ServiceBusProcessorFacade> _GetProcessors()
    {
        if (_serviceBusProcessor is not null)
        {
            yield return _serviceBusProcessor;
        }

        foreach (var processor in _queueProcessors)
        {
            yield return processor;
        }
    }

    #region private methods

    private static Dictionary<string, string?> _ConvertHeaders(ServiceBusReceivedMessage message)
    {
        var headers = message.ApplicationProperties.ToDictionary(
            x => x.Key,
            y => y.Value?.ToString(),
            StringComparer.Ordinal
        );

        return headers;
    }

    private void _ApplyCustomHeaders(ServiceBusReceivedMessage message, Dictionary<string, string?> headers)
    {
        if (_asbOptions.CustomHeadersBuilder != null)
        {
            var customHeaders = _asbOptions.CustomHeadersBuilder(message, serviceProvider);
            foreach (var customHeader in customHeaders)
            {
                var added = headers.TryAdd(customHeader.Key, customHeader.Value);

                if (!added)
                {
                    logger.CustomHeaderSkipped(customHeader.Key);
                }
            }
        }
    }

    private static TransportMessage _CreateTransportMessage(
        ServiceBusReceivedMessage message,
        string entityPath,
        Dictionary<string, string?> headers
    )
    {
        // Stamped after the custom headers builder so neither the wire nor the builder can choose the address.
        headers[Headers.TransportAddress] = entityPath;
        _ValidateRequiredHeaders(headers);
        return new TransportMessage(headers, message.Body);
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
            throw new InvalidDataException(
                "The Azure Service Bus transport envelope is missing a required Messaging header."
            );
        }
    }

    internal static void CheckValidSubscriptionName(string subscriptionName)
    {
        const char pathDelimiter = '/';
        const int ruleNameMaximumLength = 50;
        char[] invalidEntityPathCharacters = ['@', '?', '#', '*'];

        if (string.IsNullOrWhiteSpace(subscriptionName))
        {
            throw new ArgumentException("Subscribe name cannot be null or whitespace.", nameof(subscriptionName));
        }

        // "\" will be converted to "/" on the REST path anyway. Gateway/REST do not
        // have to worry about the begin/end slash problem, so this is purely a client side check.
        var tmpName = subscriptionName.Replace('\\', pathDelimiter);
        if (tmpName.Length > ruleNameMaximumLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(subscriptionName),
                $"Subscribe name '{subscriptionName}' exceeds the '{ruleNameMaximumLength}' character limit."
            );
        }

        if (tmpName.StartsWith(pathDelimiter) || tmpName.EndsWith(pathDelimiter))
        {
            throw new ArgumentException(
                $"The subscribe name cannot contain '/' as prefix or suffix. The supplied value is '{subscriptionName}'.",
                nameof(subscriptionName)
            );
        }

        if (tmpName.Contains(pathDelimiter, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"The subscribe name '{subscriptionName}' contains an invalid character '{pathDelimiter}'.",
                nameof(subscriptionName)
            );
        }

        foreach (var uriSchemeKey in invalidEntityPathCharacters)
        {
            if (subscriptionName.Contains(uriSchemeKey, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"'{subscriptionName}' contains character '{uriSchemeKey}' which is not allowed because it is reserved in the Uri scheme.",
                    nameof(subscriptionName)
                );
            }
        }
    }

    internal static void CheckValidQueueName(string queueName)
    {
        const char pathDelimiter = '/';
        const int queueNameMaximumLength = 260;
        char[] invalidEntityPathCharacters = ['@', '?', '#', '*'];

        if (string.IsNullOrWhiteSpace(queueName))
        {
            throw new ArgumentException("Queue name cannot be null or whitespace.", nameof(queueName));
        }

        var tmpName = queueName.Replace('\\', pathDelimiter);
        if (tmpName.Length > queueNameMaximumLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(queueName),
                $"Queue name '{queueName}' exceeds the '{queueNameMaximumLength}' character limit."
            );
        }

        if (tmpName.StartsWith(pathDelimiter) || tmpName.EndsWith(pathDelimiter))
        {
            throw new ArgumentException(
                $"The queue name cannot contain '/' as prefix or suffix. The supplied value is '{queueName}'.",
                nameof(queueName)
            );
        }

        foreach (var uriSchemeKey in invalidEntityPathCharacters)
        {
            if (queueName.Contains(uriSchemeKey, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"'{queueName}' contains character '{uriSchemeKey}' which is not allowed because it is reserved in the Uri scheme.",
                    nameof(queueName)
                );
            }
        }
    }

    #endregion private methods
}

internal static partial class AzureServiceBusConsumerClientLog
{
    [LoggerMessage(EventId = 3008, Level = LogLevel.Information, Message = "Azure Service Bus add rule: {NewRule}")]
    public static partial void RuleAdded(this ILogger logger, string newRule);

    [LoggerMessage(EventId = 3009, Level = LogLevel.Information, Message = "Azure Service Bus remove rule: {OldRule}")]
    public static partial void RuleRemoved(this ILogger logger, string oldRule);

    [LoggerMessage(
        EventId = 3010,
        Level = LogLevel.Information,
        Message = "Azure Service Bus created topic: {TopicPath}"
    )]
    public static partial void TopicCreated(this ILogger logger, string topicPath);

    [LoggerMessage(
        EventId = 3011,
        Level = LogLevel.Information,
        Message = "Azure Service Bus topic {TopicPath} created subscription: {SubscriptionName}"
    )]
    public static partial void SubscriptionCreated(this ILogger logger, string topicPath, string subscriptionName);

    [LoggerMessage(
        EventId = 3012,
        Level = LogLevel.Warning,
        Message = "Not possible to add the custom header {Header}. A value with the same key already exists in the Message headers."
    )]
    public static partial void CustomHeaderSkipped(this ILogger logger, string header);
}
