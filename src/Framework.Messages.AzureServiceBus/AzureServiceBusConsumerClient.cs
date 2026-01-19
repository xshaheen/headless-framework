// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using Framework.Checks;
using Framework.Messages.Helpers;
using Framework.Messages.Internal;
using Framework.Messages.Messages;
using Framework.Messages.Transport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Framework.Messages;

internal sealed class AzureServiceBusConsumerClient(
    ILogger logger,
    string subscriptionName,
    byte groupConcurrent,
    IOptions<AzureServiceBusOptions> options,
    IServiceProvider serviceProvider
) : IConsumerClient
{
    private readonly AzureServiceBusOptions _asbOptions =
        options.Value ?? throw new ArgumentNullException(nameof(options));
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private readonly SemaphoreSlim _semaphore = new(groupConcurrent);

    private ServiceBusAdministrationClient? _administrationClient;
    private ServiceBusClient? _serviceBusClient;
    private ServiceBusProcessorFacade? _serviceBusProcessor;

    public Func<TransportMessage, object?, Task>? OnMessageCallback { get; set; }

    public Action<LogMessageEventArgs>? OnLogCallback { get; set; }

    public BrokerAddress BrokerAddress =>
        ServiceBusHelpers.GetBrokerAddress(_asbOptions.ConnectionString, _asbOptions.Namespace);

    public async ValueTask SubscribeAsync(IEnumerable<string> topics)
    {
        Argument.IsNotNull(topics);

        await ConnectAsync();

        topics = topics.Concat(_asbOptions!.SqlFilters?.Select(o => o.Key) ?? []);

        var allRules = _administrationClient!.GetRulesAsync(_asbOptions!.TopicPath, subscriptionName).ToEnumerable();
        var allRuleNames = allRules.Select(o => o.Name);

        foreach (var newRule in topics.Except(allRuleNames, StringComparer.Ordinal))
        {
            var isSqlRule = _asbOptions.SqlFilters?.FirstOrDefault(o => o.Key == newRule).Value is not null;

            RuleFilter? currentRuleToAdd;

            if (isSqlRule)
            {
                var sqlExpression = _asbOptions.SqlFilters?.FirstOrDefault(o => o.Key == newRule).Value;
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

            await _administrationClient.CreateRuleAsync(
                _asbOptions.TopicPath,
                subscriptionName,
                new CreateRuleOptions { Name = newRule, Filter = currentRuleToAdd }
            );

            logger.LogInformation("Azure Service Bus add rule: {NewRule}", newRule);
        }

        foreach (var oldRule in allRuleNames.Except(topics, StringComparer.Ordinal))
        {
            await _administrationClient.DeleteRuleAsync(_asbOptions.TopicPath, subscriptionName, oldRule);

            logger.LogInformation("Azure Service Bus remove rule: {OldRule}", oldRule);
        }
    }

    public async ValueTask ListeningAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        await ConnectAsync();

        if (_serviceBusProcessor!.IsSessionProcessor)
        {
            _serviceBusProcessor!.ProcessSessionMessageAsync += _serviceBusProcessor_ProcessSessionMessageAsync;
        }
        else
        {
            _serviceBusProcessor!.ProcessMessageAsync += _serviceBusProcessor_ProcessMessageAsync;
        }

        _serviceBusProcessor.ProcessErrorAsync += _serviceBusProcessor_ProcessErrorAsync;

        await _serviceBusProcessor.StartProcessingAsync(cancellationToken);
    }

    public async ValueTask CommitAsync(object? sender)
    {
        var commitInput = (AzureServiceBusConsumerCommitInput)sender!;
        if (!_serviceBusProcessor!.AutoCompleteMessages)
        {
            await commitInput.CompleteMessageAsync();
        }

        _semaphore.Release();
    }

    public async ValueTask RejectAsync(object? sender)
    {
        var commitInput = (AzureServiceBusConsumerCommitInput)sender!;
        await commitInput.AbandonMessageAsync();
        _semaphore.Release();
    }

    public async ValueTask DisposeAsync()
    {
        if (!_serviceBusProcessor!.IsProcessing)
        {
            await _serviceBusProcessor.DisposeAsync();
        }
    }

    private Task _serviceBusProcessor_ProcessErrorAsync(ProcessErrorEventArgs args)
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

    private async Task _serviceBusProcessor_ProcessMessageAsync(ProcessMessageEventArgs arg)
    {
        var context = _ConvertMessage(arg.Message);

        if (groupConcurrent > 0)
        {
            await _semaphore.WaitAsync();
            _ = Task.Run(() => OnMessageCallback!(context, new AzureServiceBusConsumerCommitInput(arg)))
                .ConfigureAwait(false);
        }
        else
        {
            await OnMessageCallback!(context, new AzureServiceBusConsumerCommitInput(arg));
        }
    }

    private async Task _serviceBusProcessor_ProcessSessionMessageAsync(ProcessSessionMessageEventArgs arg)
    {
        var context = _ConvertMessage(arg.Message);

        await OnMessageCallback!(context, new AzureServiceBusConsumerCommitInput(arg));
    }

    public async Task ConnectAsync()
    {
        if (_serviceBusProcessor != null)
        {
            return;
        }

        await _connectionLock.WaitAsync();

        try
        {
            if (_serviceBusProcessor == null)
            {
                if (_asbOptions.TokenCredential != null)
                {
                    _administrationClient = new ServiceBusAdministrationClient(
                        _asbOptions.Namespace,
                        _asbOptions.TokenCredential
                    );
                    _serviceBusClient = new ServiceBusClient(_asbOptions.Namespace, _asbOptions.TokenCredential);
                }
                else
                {
                    _administrationClient = new ServiceBusAdministrationClient(_asbOptions.ConnectionString);
                    _serviceBusClient = new ServiceBusClient(_asbOptions.ConnectionString);
                }

                var topicConfigs = _asbOptions
                    .CustomProducers.Select(producer =>
                        (topicPaths: producer.TopicPath, subscribe: producer.CreateSubscription)
                    )
                    .Append((topicPaths: _asbOptions.TopicPath, subscribe: true))
                    .GroupBy(n => n.topicPaths, StringComparer.OrdinalIgnoreCase)
                    .Select(n => (topicPaths: n.Key, subscribe: n.Max(o => o.subscribe)));

                foreach (var (topicPath, subscribe) in topicConfigs)
                {
                    if (!await _administrationClient.TopicExistsAsync(topicPath))
                    {
                        await _administrationClient.CreateTopicAsync(topicPath);
                        logger.LogInformation("Azure Service Bus created topic: {TopicPath}", topicPath);
                    }

                    if (subscribe && !await _administrationClient.SubscriptionExistsAsync(topicPath, subscriptionName))
                    {
                        var subscriptionDescription = new CreateSubscriptionOptions(topicPath, subscriptionName)
                        {
                            RequiresSession = _asbOptions.EnableSessions,
                            AutoDeleteOnIdle = _asbOptions.SubscriptionAutoDeleteOnIdle,
                            LockDuration = _asbOptions.SubscriptionMessageLockDuration,
                            DefaultMessageTimeToLive = _asbOptions.SubscriptionDefaultMessageTimeToLive,
                            MaxDeliveryCount = _asbOptions.SubscriptionMaxDeliveryCount,
                        };

                        await _administrationClient.CreateSubscriptionAsync(subscriptionDescription);

                        logger.LogInformation(
                            "Azure Service Bus topic {TopicPath} created subscription: {SubscriptionName}",
                            topicPath,
                            subscriptionName
                        );
                    }
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
                    logger.LogWarning(
                        "Not possible to add the custom header {Header}. A value with the same key already exists in the Message headers.",
                        customHeader.Key
                    );
                }
            }
        }

        return new TransportMessage(headers, message.Body);
    }

    private static void _CheckValidSubscriptionName(string subscriptionName)
    {
        const string pathDelimiter = @"/";
        const int ruleNameMaximumLength = 50;
        char[] invalidEntityPathCharacters = ['@', '?', '#', '*'];

        if (string.IsNullOrWhiteSpace(subscriptionName))
        {
            throw new ArgumentNullException(subscriptionName);
        }

        // and "\" will be converted to "/" on the REST path anyway. Gateway/REST do not
        // have to worry about the begin/end slash problem, so this is purely a client side check.
        var tmpName = subscriptionName.Replace(@"\", pathDelimiter);
        if (tmpName.Length > ruleNameMaximumLength)
        {
            throw new ArgumentOutOfRangeException(
                subscriptionName,
                $@"Subscribe name '{subscriptionName}' exceeds the '{ruleNameMaximumLength}' character limit."
            );
        }

        if (
            tmpName.StartsWith(pathDelimiter, StringComparison.OrdinalIgnoreCase)
            || tmpName.EndsWith(pathDelimiter, StringComparison.OrdinalIgnoreCase)
        )
        {
            throw new ArgumentException(
                $@"The subscribe name cannot contain '/' as prefix or suffix. The supplied value is '{subscriptionName}'",
                subscriptionName
            );
        }

        if (tmpName.Contains(pathDelimiter))
        {
            throw new ArgumentException(
                $@"The subscribe name contains an invalid character '{pathDelimiter}'",
                subscriptionName
            );
        }

        foreach (var uriSchemeKey in invalidEntityPathCharacters)
        {
            if (subscriptionName.Contains(uriSchemeKey, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $@"'{subscriptionName}' contains character '{uriSchemeKey}' which is not allowed because it is reserved in the Uri scheme.",
                    subscriptionName
                );
            }
        }
    }

    #endregion private methods
}
