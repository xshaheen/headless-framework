// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Headless.Checks;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Messaging.Transport;
using Microsoft.Extensions.Options;
using Headers = Headless.Messaging.Messages.Headers;

namespace Headless.Messaging.Kafka;

public sealed class KafkaConsumerClient(
    string groupId,
    byte groupConcurrent,
    IOptions<MessagingKafkaOptions> options,
    IServiceProvider serviceProvider
) : IConsumerClient
{
    private readonly Lock _lock = new();
    private readonly SemaphoreSlim _semaphore = new(groupConcurrent);
    private readonly MessagingKafkaOptions _kafkaOptions = Argument.IsNotNull(options.Value);
    private IConsumer<string, byte[]>? _consumerClient;

    public Func<TransportMessage, object?, Task>? OnMessageCallback { get; set; }

    public Action<LogMessageEventArgs>? OnLogCallback { get; set; }

    public BrokerAddress BrokerAddress => new("kafka", _kafkaOptions.Servers);

    public async Task<ICollection<string>> FetchTopicsAsync(IEnumerable<string> topicNames)
    {
        Argument.IsNotNull(topicNames);

        var regexTopicNames = topicNames.Select(Helper.WildcardToRegex).ToList();

        var allowAutoCreate = true;
        if (
            _kafkaOptions.MainConfig.TryGetValue("allow.auto.create.topics", out var autoCreateValue)
            && bool.TryParse(autoCreateValue, out var parsedValue)
        )
        {
            allowAutoCreate = parsedValue;
        }

        if (allowAutoCreate)
        {
            try
            {
                var config = new AdminClientConfig(_kafkaOptions.MainConfig)
                {
                    BootstrapServers = _kafkaOptions.Servers,
                };

                using var adminClient = new AdminClientBuilder(config).Build();

                await adminClient.CreateTopicsAsync(
                    regexTopicNames.Select(x => new TopicSpecification
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

        return regexTopicNames;
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

        while (!cancellationToken.IsCancellationRequested)
        {
            ConsumeResult<string, byte[]> consumerResult;

            try
            {
                consumerResult = _consumerClient!.Consume(timeout);

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

            if (groupConcurrent > 0)
            {
                await _semaphore.WaitAsync(cancellationToken);
                _ = Task.Run(() => _ConsumeAsync(consumerResult), cancellationToken).AnyContext();
            }
            else
            {
                await _ConsumeAsync(consumerResult);
            }
        }
        // ReSharper disable once FunctionNeverReturns
    }

    public ValueTask CommitAsync(object? sender)
    {
        _consumerClient!.Commit((ConsumeResult<string, byte[]>)sender!);
        _semaphore.Release();
        return ValueTask.CompletedTask;
    }

    public ValueTask RejectAsync(object? sender)
    {
        _consumerClient!.Assign(_consumerClient.Assignment);
        _semaphore.Release();
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _semaphore.Dispose();
        _consumerClient?.Dispose();
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
                config.GroupId ??= groupId;
                config.AutoOffsetReset ??= AutoOffsetReset.Earliest;
                config.AllowAutoCreateTopics ??= true;
                config.EnableAutoCommit ??= false;
                config.LogConnectionClose ??= false;

                _consumerClient = _BuildConsumer(config);
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

        headers[Headers.Group] = groupId;

        if (_kafkaOptions.CustomHeadersBuilder != null)
        {
            var customHeaders = _kafkaOptions.CustomHeadersBuilder(consumerResult, serviceProvider);
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
