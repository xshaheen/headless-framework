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
    private readonly ConsumerPauseGate _pauseGate = new();
    private int _disposed;
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
                await _pauseGate.WaitIfPausedAsync(cancellationToken).ConfigureAwait(false);

                var consumerResult = await _consumerClient!.ReceiveAsync(cancellationToken);

                if (groupConcurrent > 0)
                {
                    await _semaphore.WaitAsync(cancellationToken);
                    _ = Task.Run(
                        async () =>
                        {
                            try
                            {
                                await consumeAsync();
                            }
                            finally
                            {
                                _ReleaseSemaphore();
                            }
                        },
                        cancellationToken
                    ).ConfigureAwait(false);
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
    }

    public async ValueTask RejectAsync(object? sender)
    {
        if (sender is MessageId id)
        {
            await _consumerClient!.NegativeAcknowledge(id);
        }
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

    public async ValueTask PauseAsync(CancellationToken cancellationToken = default) => await _pauseGate.PauseAsync();

    public async ValueTask ResumeAsync(CancellationToken cancellationToken = default) => await _pauseGate.ResumeAsync();

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return ValueTask.CompletedTask;

        _pauseGate.Release();
        _semaphore.Dispose();
        return _consumerClient?.DisposeAsync() ?? ValueTask.CompletedTask;
    }
}
