// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Headless.Checks;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Messaging.Transport;
using Microsoft.Extensions.Options;
using Headers = Headless.Messaging.Headers;

namespace Headless.Messaging.Kafka;

internal sealed class KafkaConsumerClient : IConsumerClient
{
    private readonly string _groupId;
    private readonly Lock _lock = new();
    private readonly MessagingKafkaOptions _kafkaOptions;
    private readonly ConsumerPauseGate _pauseGate = new();
    private readonly byte _requestedConcurrency;
    private readonly IServiceProvider _serviceProvider;
    private readonly Func<ConsumerConfig, IConsumer<string, byte[]>> _consumerFactory;
    private readonly Func<AdminClientConfig, IAdminClient> _adminClientFactory;
    private IConsumer<string, byte[]>? _consumerClient;
    private int _configurationWarningLogged;
    private int _disposed;

    public KafkaConsumerClient(
        string groupId,
        byte groupConcurrent,
        IOptions<MessagingKafkaOptions> options,
        IServiceProvider serviceProvider,
        Func<ConsumerConfig, IConsumer<string, byte[]>>? consumerFactory = null,
        Func<AdminClientConfig, IAdminClient>? adminClientFactory = null
    )
    {
        _groupId = groupId;
        _kafkaOptions = Argument.IsNotNull(options.Value);
        _requestedConcurrency = groupConcurrent;
        _serviceProvider = serviceProvider;
        _consumerFactory = consumerFactory ?? _BuildConsumer;
        _adminClientFactory = adminClientFactory ?? _BuildAdminClient;
    }

    public Func<TransportMessage, object?, Task>? OnMessageCallback { get; set; }

    public Action<LogMessageEventArgs>? OnLogCallback { get; set; }

    public BrokerAddress BrokerAddress =>
        new("kafka", BrokerAddressDisplay.GetDisplayEndpoints(_kafkaOptions.Servers, inferredScheme: "kafka"));

    public async ValueTask<ICollection<string>> FetchTopicsAsync(IEnumerable<string> topicNames)
    {
        Argument.IsNotNull(topicNames);

        var normalizedTopics = new List<string>();
        var concreteTopicsToCreate = new List<string>();

        foreach (var topicName in topicNames)
        {
            if (topicName.Contains('*') || topicName.Contains('#'))
            {
                normalizedTopics.Add(Helper.WildcardToRegex(topicName));
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

                await adminClient.CreateTopicsAsync(
                    concreteTopicsToCreate.Select(x => new TopicSpecification
                    {
                        Name = x,
                        NumPartitions = _kafkaOptions.TopicOptions.NumPartitions,
                        ReplicationFactor = _kafkaOptions.TopicOptions.ReplicationFactor,
                    })
                );
            }
#pragma warning disable ERP022
            catch (CreateTopicsException e) when (e.Message.Contains("already exists", StringComparison.Ordinal)) { }
            catch (Exception e)
            {
                var logArgs = new LogMessageEventArgs
                {
                    LogType = MqLogType.ConsumeError,
                    Reason = "An error was encountered when automatically creating topic! -->" + e,
                };
                OnLogCallback!(logArgs);
            }
#pragma warning restore ERP022
        }

        return normalizedTopics;
    }

    public ValueTask SubscribeAsync(IEnumerable<string> topics)
    {
        Argument.IsNotNull(topics);

        Connect();

        _consumerClient!.Subscribe(topics);

        return ValueTask.CompletedTask;
    }

    public async ValueTask ListeningAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        Connect();
        _LogSequentialProcessingConfigurationWarningIfNeeded();

        while (!cancellationToken.IsCancellationRequested)
        {
            await _pauseGate.WaitIfPausedAsync(cancellationToken).ConfigureAwait(false);

            ConsumeResult<string, byte[]> consumerResult;

            try
            {
                lock (_lock)
                {
                    consumerResult = _consumerClient!.Consume(timeout);
                }

                if (consumerResult == null)
                {
                    continue;
                }

                if (consumerResult.IsPartitionEOF || consumerResult.Message.Value == null)
                {
                    continue;
                }
            }
            catch (ConsumeException e) when (_kafkaOptions.RetriableErrorCodes.Contains(e.Error.Code))
            {
                var logArgs = new LogMessageEventArgs
                {
                    LogType = MqLogType.ConsumeRetries,
                    Reason = e.Error.ToString(),
                };
                OnLogCallback!(logArgs);

                continue;
            }

            // Kafka commits advance partition offsets. Processing sequentially within a consumer
            // prevents later messages from committing past earlier failures on the same partition.
            await _ConsumeAsync(consumerResult);
        }
        // ReSharper disable once FunctionNeverReturns
    }

    public ValueTask CommitAsync(object? sender)
    {
        if (sender is not ConsumeResult<string, byte[]> consumerResult || _consumerClient is null)
        {
            return ValueTask.CompletedTask;
        }

        lock (_lock)
        {
            _consumerClient.Commit(consumerResult);
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask RejectAsync(object? sender)
    {
        if (sender is not ConsumeResult<string, byte[]> consumerResult || _consumerClient is null)
        {
            return ValueTask.CompletedTask;
        }

        lock (_lock)
        {
            _consumerClient.Seek(consumerResult.TopicPartitionOffset);
        }

        return ValueTask.CompletedTask;
    }

    public async ValueTask PauseAsync(CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref _disposed) != 0)
            return;
        if (!await _pauseGate.PauseAsync())
            return;

        lock (_lock)
        {
            _consumerClient?.Pause(_consumerClient.Assignment);
        }
    }

    public async ValueTask ResumeAsync(CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref _disposed) != 0)
            return;
        if (!await _pauseGate.ResumeAsync())
            return;

        lock (_lock)
        {
            _consumerClient?.Resume(_consumerClient.Assignment);
        }
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return ValueTask.CompletedTask;

        _pauseGate.Release();
        IConsumer<string, byte[]>? consumerClient;
        lock (_lock)
        {
            consumerClient = _consumerClient;
            _consumerClient = null;
        }

        if (consumerClient is not null)
        {
            try
            {
                consumerClient.Close();
            }
            catch (Exception)
            {
                // Best-effort shutdown. Dispose still releases native resources.
            }

            consumerClient.Dispose();
        }

        return ValueTask.CompletedTask;
    }

    public void Connect()
    {
        if (_consumerClient != null)
        {
            return;
        }

        lock (_lock)
        {
#pragma warning disable CA1508 // Justification: other thread can initialize it
            if (_consumerClient == null)
#pragma warning restore CA1508
            {
                var config = new ConsumerConfig(
                    new Dictionary<string, string>(_kafkaOptions.MainConfig, StringComparer.Ordinal)
                );
                config.BootstrapServers ??= _kafkaOptions.Servers;
                config.GroupId ??= _groupId;
                config.AutoOffsetReset ??= AutoOffsetReset.Earliest;
                config.AllowAutoCreateTopics ??= true;
                config.EnableAutoCommit ??= false;
                config.LogConnectionClose ??= false;

                _consumerClient = _consumerFactory(config);
            }
        }
    }

    private async Task _ConsumeAsync(ConsumeResult<string, byte[]> consumerResult)
    {
        var headers = new Dictionary<string, string?>(consumerResult.Message.Headers.Count, StringComparer.Ordinal);
        foreach (var header in consumerResult.Message.Headers)
        {
            var val = header.GetValueBytes();
            headers[header.Key] = val != null ? Encoding.UTF8.GetString(val) : null;
        }

        headers[Headers.Group] = _groupId;

        if (_kafkaOptions.CustomHeadersBuilder != null)
        {
            var customHeaders = _kafkaOptions.CustomHeadersBuilder(consumerResult, _serviceProvider);
            foreach (var customHeader in customHeaders)
            {
                headers[customHeader.Key] = customHeader.Value;
            }
        }

        var message = new TransportMessage(headers, consumerResult.Message.Value);

        await OnMessageCallback!(message, consumerResult);
    }

    private IConsumer<string, byte[]> _BuildConsumer(ConsumerConfig config)
    {
        return new ConsumerBuilder<string, byte[]>(config).SetErrorHandler(_ConsumerClientOnConsumeError).Build();
    }

    private void _LogSequentialProcessingConfigurationWarningIfNeeded()
    {
        if (_requestedConcurrency <= 1 || Interlocked.CompareExchange(ref _configurationWarningLogged, 1, 0) != 0)
        {
            return;
        }

        OnLogCallback?.Invoke(
            new LogMessageEventArgs
            {
                LogType = MqLogType.TransportConfigurationWarning,
                Reason =
                    $"Kafka transport processes messages sequentially to preserve offset commit ordering; requested groupConcurrent={_requestedConcurrency} is ignored.",
            }
        );
    }

    private static IAdminClient _BuildAdminClient(AdminClientConfig config)
    {
        return new AdminClientBuilder(config).Build();
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
}
