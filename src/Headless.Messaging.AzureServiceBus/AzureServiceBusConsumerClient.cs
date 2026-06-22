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
    IOptions<AzureServiceBusOptions> options,
    IServiceProvider serviceProvider,
    IntentType intentType = IntentType.Bus
) : IConsumerClient
{
    private readonly AzureServiceBusOptions _asbOptions = Argument.IsNotNull(options.Value);
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private readonly SemaphoreSlim _semaphore = new(groupConcurrent);
    private readonly ConsumerPauseGate _pauseGate = new();
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private int _disposed;
    private int _hasStartedProcessing;
    private ServiceBusAdministrationClient? _administrationClient;
    private ServiceBusClient? _serviceBusClient;
    private ServiceBusProcessorFacade? _serviceBusProcessor;
    private readonly List<ServiceBusProcessorFacade> _queueProcessors = [];

    public Func<TransportMessage, object?, Task>? OnMessageCallback { get; set; }

    public Action<LogMessageEventArgs>? OnLogCallback { get; set; }

    public BrokerAddress BrokerAddress =>
        ServiceBusHelpers.GetBrokerAddress(_asbOptions.ConnectionString, _asbOptions.Namespace);

    public async ValueTask SubscribeAsync(IEnumerable<string> messageNames)
    {
        Argument.IsNotNull(messageNames);

        await ConnectAsync().ConfigureAwait(false);

        if (intentType == IntentType.Queue)
        {
            foreach (var messageName in messageNames)
            {
                CheckValidQueueName(messageName);
                await _EnsureQueueProcessorAsync(messageName).ConfigureAwait(false);
            }

            return;
        }

        if (!_asbOptions.AutoProvision)
        {
            return;
        }

        // Get existing rules

        var allRuleNames = new List<string>();
        var allRules = _administrationClient!.GetRulesAsync(_asbOptions.TopicPath, subscriptionName);

        await foreach (var rule in allRules)
        {
            allRuleNames.Add(rule.Name);
        }

        var messageNamesList = messageNames.Concat(_asbOptions.SqlFilters?.Select(o => o.Key) ?? []).ToList();

        foreach (var newRule in messageNamesList.Except(allRuleNames, StringComparer.Ordinal))
        {
            var isSqlRule =
                _asbOptions
                    .SqlFilters?.FirstOrDefault(o => string.Equals(o.Key, newRule, StringComparison.Ordinal))
                    .Value
                is not null;

            RuleFilter? currentRuleToAdd;

            if (isSqlRule)
            {
                var sqlExpression = _asbOptions
                    .SqlFilters?.FirstOrDefault(o => string.Equals(o.Key, newRule, StringComparison.Ordinal))
                    .Value;
                currentRuleToAdd = new SqlRuleFilter(sqlExpression);
            }
            else
            {
                var correlationRule = new CorrelationRuleFilter { Subject = newRule };

                foreach (var correlationHeader in _asbOptions.DefaultCorrelationHeaders)
                {
                    correlationRule.ApplicationProperties.Add(correlationHeader.Key, correlationHeader.Value);
                }

                currentRuleToAdd = correlationRule;
            }

            await _administrationClient
                .CreateRuleAsync(
                    _asbOptions.TopicPath,
                    subscriptionName,
                    new CreateRuleOptions { Name = newRule, Filter = currentRuleToAdd }
                )
                .ConfigureAwait(false);

            logger.RuleAdded(newRule);
        }

        foreach (var oldRule in allRuleNames.Except(messageNamesList, StringComparer.Ordinal))
        {
            await _administrationClient
                .DeleteRuleAsync(_asbOptions.TopicPath, subscriptionName, oldRule)
                .ConfigureAwait(false);

            logger.RuleRemoved(oldRule);
        }
    }

    public async ValueTask ListeningAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        await ConnectAsync().ConfigureAwait(false);

        IReadOnlyList<ServiceBusProcessorFacade> processors =
            intentType == IntentType.Queue ? _queueProcessors : [_serviceBusProcessor!];

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

    public async ValueTask CommitAsync(object? sender)
    {
        var commitInput = (AzureServiceBusConsumerCommitInput)sender!;
        if (!_asbOptions.AutoCompleteMessages)
        {
            await commitInput.CompleteMessageAsync().ConfigureAwait(false);
        }
    }

    public async ValueTask RejectAsync(object? sender)
    {
        var commitInput = (AzureServiceBusConsumerCommitInput)sender!;
        await commitInput.AbandonMessageAsync().ConfigureAwait(false);
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

        if (_serviceBusClient is not null)
        {
            await _serviceBusClient.DisposeAsync().ConfigureAwait(false);
        }

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

        return Task.CompletedTask;
    }

    private async Task _ServiceBusProcessor_ProcessMessageAsync(ProcessMessageEventArgs arg)
    {
        var context = _ConvertMessage(arg.Message);

        if (groupConcurrent > 0)
        {
            await _semaphore.WaitAsync(arg.CancellationToken).ConfigureAwait(false);
            try
            {
                await OnMessageCallback!(context, new AzureServiceBusConsumerCommitInput(arg)).ConfigureAwait(false);
            }
            finally
            {
                _ReleaseSemaphore();
            }
        }
        else
        {
            await OnMessageCallback!(context, new AzureServiceBusConsumerCommitInput(arg)).ConfigureAwait(false);
        }
    }

    private async Task _ServiceBusProcessor_ProcessSessionMessageAsync(ProcessSessionMessageEventArgs arg)
    {
        var context = _ConvertMessage(arg.Message);

        await OnMessageCallback!(context, new AzureServiceBusConsumerCommitInput(arg)).ConfigureAwait(false);
    }

    public async Task ConnectAsync()
    {
        if (_serviceBusProcessor != null || (intentType == IntentType.Queue && _serviceBusClient != null))
        {
            return;
        }

        await _connectionLock.WaitAsync().ConfigureAwait(false);

        try
        {
#pragma warning disable CA1508 // Justification: other thread can initialize it
            if (_serviceBusProcessor == null && (intentType != IntentType.Queue || _serviceBusClient == null))
#pragma warning restore CA1508
            {
                _serviceBusClient = _asbOptions.TokenCredential is not null
                    ? new ServiceBusClient(_asbOptions.Namespace, _asbOptions.TokenCredential)
                    : new ServiceBusClient(_asbOptions.ConnectionString);

                if (_asbOptions.AutoProvision)
                {
                    _administrationClient = _asbOptions.TokenCredential is not null
                        ? new ServiceBusAdministrationClient(_asbOptions.Namespace, _asbOptions.TokenCredential)
                        : new ServiceBusAdministrationClient(_asbOptions.ConnectionString);
                }

                if (intentType == IntentType.Bus && _asbOptions.AutoProvision)
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
                        if (!await administrationClient.TopicExistsAsync(topicPath).ConfigureAwait(false))
                        {
                            await administrationClient.CreateTopicAsync(topicPath).ConfigureAwait(false);
                            logger.TopicCreated(topicPath);
                        }

                        if (
                            subscribe
                            && !await administrationClient
                                .SubscriptionExistsAsync(topicPath, subscriptionName)
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
                                .CreateSubscriptionAsync(subscriptionDescription)
                                .ConfigureAwait(false);

                            logger.SubscriptionCreated(topicPath, subscriptionName);
                        }
                    }
                }

                if (intentType == IntentType.Queue)
                {
                    return;
                }

                _serviceBusProcessor = !_asbOptions.EnableSessions
                    ? new ServiceBusProcessorFacade(
                        serviceBusProcessor: _serviceBusClient.CreateProcessor(
                            _asbOptions.TopicPath,
                            subscriptionName,
                            new ServiceBusProcessorOptions
                            {
                                AutoCompleteMessages = _asbOptions.AutoCompleteMessages,
                                MaxConcurrentCalls = _asbOptions.MaxConcurrentCalls,
                                MaxAutoLockRenewalDuration = _asbOptions.MaxAutoLockRenewalDuration,
                            }
                        )
                    )
                    : new ServiceBusProcessorFacade(
                        serviceBusSessionProcessor: _serviceBusClient.CreateSessionProcessor(
                            _asbOptions.TopicPath,
                            subscriptionName,
                            new ServiceBusSessionProcessorOptions
                            {
                                AutoCompleteMessages = _asbOptions.AutoCompleteMessages,
                                MaxConcurrentCallsPerSession = _asbOptions.MaxConcurrentCalls,
                                MaxAutoLockRenewalDuration = _asbOptions.MaxAutoLockRenewalDuration,
                                MaxConcurrentSessions = _asbOptions.MaxConcurrentSessions,
                                SessionIdleTimeout = _asbOptions.SessionIdleTimeout,
                            }
                        )
                    );
            }
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    private async Task _EnsureQueueProcessorAsync(string queueName)
    {
        await ConnectAsync().ConfigureAwait(false);

        if (_asbOptions.AutoProvision && _administrationClient is not null)
        {
            if (!await _administrationClient.QueueExistsAsync(queueName).ConfigureAwait(false))
            {
                var queueOptions = new CreateQueueOptions(queueName)
                {
                    RequiresSession = _asbOptions.EnableSessions,
                    AutoDeleteOnIdle = _asbOptions.SubscriptionAutoDeleteOnIdle,
                    LockDuration = _asbOptions.SubscriptionMessageLockDuration,
                    DefaultMessageTimeToLive = _asbOptions.SubscriptionDefaultMessageTimeToLive,
                    MaxDeliveryCount = _asbOptions.SubscriptionMaxDeliveryCount,
                };

                await _administrationClient.CreateQueueAsync(queueOptions).ConfigureAwait(false);
            }
        }

        var processor = !_asbOptions.EnableSessions
            ? new ServiceBusProcessorFacade(
                serviceBusProcessor: _serviceBusClient!.CreateProcessor(
                    queueName,
                    new ServiceBusProcessorOptions
                    {
                        AutoCompleteMessages = _asbOptions.AutoCompleteMessages,
                        MaxConcurrentCalls = _asbOptions.MaxConcurrentCalls,
                        MaxAutoLockRenewalDuration = _asbOptions.MaxAutoLockRenewalDuration,
                    }
                )
            )
            : new ServiceBusProcessorFacade(
                serviceBusSessionProcessor: _serviceBusClient!.CreateSessionProcessor(
                    queueName,
                    new ServiceBusSessionProcessorOptions
                    {
                        AutoCompleteMessages = _asbOptions.AutoCompleteMessages,
                        MaxConcurrentCallsPerSession = _asbOptions.MaxConcurrentCalls,
                        MaxAutoLockRenewalDuration = _asbOptions.MaxAutoLockRenewalDuration,
                        MaxConcurrentSessions = _asbOptions.MaxConcurrentSessions,
                        SessionIdleTimeout = _asbOptions.SessionIdleTimeout,
                    }
                )
            );

        _queueProcessors.Add(processor);
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

    private TransportMessage _ConvertMessage(ServiceBusReceivedMessage message)
    {
        var headers = message.ApplicationProperties.ToDictionary(
            x => x.Key,
            y => y.Value?.ToString(),
            StringComparer.Ordinal
        );

        headers[Headers.Group] = subscriptionName;

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

        return new TransportMessage(headers, message.Body);
    }

    internal static void CheckValidSubscriptionName(string subscriptionName)
    {
        const string pathDelimiter = "/";
        const int ruleNameMaximumLength = 50;
        char[] invalidEntityPathCharacters = ['@', '?', '#', '*'];

        if (string.IsNullOrWhiteSpace(subscriptionName))
        {
            throw new ArgumentException("Subscribe name cannot be null or whitespace.", nameof(subscriptionName));
        }

        // "\" will be converted to "/" on the REST path anyway. Gateway/REST do not
        // have to worry about the begin/end slash problem, so this is purely a client side check.
        var tmpName = subscriptionName.Replace(@"\", pathDelimiter, StringComparison.Ordinal);
        if (tmpName.Length > ruleNameMaximumLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(subscriptionName),
                $"Subscribe name '{subscriptionName}' exceeds the '{ruleNameMaximumLength}' character limit."
            );
        }

        if (
            tmpName.StartsWith(pathDelimiter, StringComparison.Ordinal)
            || tmpName.EndsWith(pathDelimiter, StringComparison.Ordinal)
        )
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
        const string pathDelimiter = "/";
        const int queueNameMaximumLength = 260;
        char[] invalidEntityPathCharacters = ['@', '?', '#', '*'];

        if (string.IsNullOrWhiteSpace(queueName))
        {
            throw new ArgumentException("Queue name cannot be null or whitespace.", nameof(queueName));
        }

        var tmpName = queueName.Replace(@"\", pathDelimiter, StringComparison.Ordinal);
        if (tmpName.Length > queueNameMaximumLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(queueName),
                $"Queue name '{queueName}' exceeds the '{queueNameMaximumLength}' character limit."
            );
        }

        if (
            tmpName.StartsWith(pathDelimiter, StringComparison.Ordinal)
            || tmpName.EndsWith(pathDelimiter, StringComparison.Ordinal)
        )
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
