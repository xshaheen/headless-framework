// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Headless.Checks;
using Headless.Messaging.Messages;
using Headless.Messaging.Transport;
using Microsoft.Extensions.Options;
using Pulsar.Client.Api;
using Pulsar.Client.Common;

namespace Headless.Messaging.Pulsar;

internal sealed class PulsarConsumerClient(
    IOptions<MessagingPulsarOptions> options,
    PulsarClient client,
    string groupName,
    byte groupConcurrent
) : IConsumerClient
{
    private readonly SemaphoreSlim _semaphore = new(groupConcurrent);
    private readonly MessagingPulsarOptions _pulsarOptions = options.Value;
    private IConsumer<byte[]>? _consumerClient;

    public Func<TransportMessage, object?, Task>? OnMessageCallback { get; set; }

    public Action<LogMessageEventArgs>? OnLogCallback { get; set; }

    public BrokerAddress BrokerAddress => new("pulsar", _pulsarOptions.ServiceUrl);

    public async ValueTask SubscribeAsync(IEnumerable<string> topics)
    {
        Argument.IsNotNull(topics);

        var serviceName = Assembly.GetEntryAssembly()?.GetName().Name!.ToLowerInvariant();

        _consumerClient = await client
            .NewConsumer()
            .Topics(topics)
            .SubscriptionName(groupName)
            .ConsumerName(serviceName)
            .SubscriptionType(SubscriptionType.Shared)
            .SubscribeAsync();
    }

    public async ValueTask ListeningAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var consumerResult = await _consumerClient!.ReceiveAsync(cancellationToken);

                if (groupConcurrent > 0)
                {
                    await _semaphore.WaitAsync(cancellationToken);
                    _ = Task.Run(consumeAsync, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await consumeAsync();
                }

                Task consumeAsync()
                {
                    var headers = new Dictionary<string, string?>(
                        consumerResult.Properties.Count,
                        StringComparer.Ordinal
                    );
                    foreach (var header in consumerResult.Properties)
                    {
                        headers.Add(header.Key, header.Value);
                    }

                    headers[Headers.Group] = groupName;

                    var message = new TransportMessage(headers, consumerResult.Data);

                    return OnMessageCallback!(message, consumerResult.MessageId);
                }
            }
            catch (Exception e)
            {
                OnLogCallback!(new LogMessageEventArgs { LogType = MqLogType.ConsumeError, Reason = e.Message });
            }
        }
    }

    public async ValueTask CommitAsync(object? sender)
    {
        await _consumerClient!.AcknowledgeAsync((MessageId)sender!);
        _semaphore.Release();
    }

    public async ValueTask RejectAsync(object? sender)
    {
        if (sender is MessageId id)
        {
            await _consumerClient!.NegativeAcknowledge(id);
        }

        _semaphore.Release();
    }

    public ValueTask DisposeAsync()
    {
        _semaphore.Dispose();
        return _consumerClient?.DisposeAsync() ?? ValueTask.CompletedTask;
    }
}
