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

    public BrokerAddress BrokerAddress => new("pulsar", BrokerAddressDisplay.Format(_pulsarOptions.ServiceUrl));

    public async ValueTask SubscribeAsync(IEnumerable<string> topics)
    {
        Argument.IsNotNull(topics);

        var serviceName = Assembly.GetEntryAssembly()?.GetName().Name!.ToLowerInvariant();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        _consumerClient = await client
            .NewConsumer()
            .Topics(topics)
            .SubscriptionName(groupName)
            .ConsumerName(serviceName)
            .SubscriptionType(SubscriptionType.Shared)
            .SubscribeAsync()
            .WaitAsync(cts.Token);
    }

    public async ValueTask ListeningAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            Message<byte[]> consumerResult;
            try
            {
                await _pauseGate.WaitIfPausedAsync(cancellationToken).ConfigureAwait(false);
                consumerResult = await _consumerClient!.ReceiveAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception e)
            {
                OnLogCallback!(new LogMessageEventArgs { LogType = MqLogType.ConsumeError, Reason = e.Message });
                continue;
            }

            if (groupConcurrent > 0)
            {
                await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                _ObserveBackgroundHandler(
                    Task.Run(
                        async () =>
                        {
                            try
                            {
                                await consumeAsync(consumerResult).ConfigureAwait(false);
                            }
                            finally
                            {
                                _ReleaseSemaphore();
                            }
                        },
                        CancellationToken.None // Ensure semaphore release even if cancellation is requested during handler execution
                    )
                );
            }
            else
            {
                await consumeAsync(consumerResult).ConfigureAwait(false);
            }

            async Task consumeAsync(Message<byte[]> currentMessage)
            {
                TransportMessage message;
                try
                {
                    var headers = new Dictionary<string, string?>(
                        currentMessage.Properties.Count,
                        StringComparer.Ordinal
                    );
                    foreach (var header in currentMessage.Properties)
                    {
                        headers.Add(header.Key, header.Value);
                    }

                    headers[Headers.Group] = groupName;

                    message = new TransportMessage(headers, currentMessage.Data);
                }
                catch (Exception ex)
                {
                    OnLogCallback!(
                        new LogMessageEventArgs
                        {
                            LogType = MqLogType.ConsumeError,
                            Reason = $"Failed to build transport message, nacking: {ex}",
                        }
                    );

                    await RejectAsync(currentMessage.MessageId).ConfigureAwait(false);
                    return;
                }

                await OnMessageCallback!(message, currentMessage.MessageId).ConfigureAwait(false);
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

    public async ValueTask PauseAsync(CancellationToken cancellationToken = default) => await _pauseGate.PauseAsync();

    public async ValueTask ResumeAsync(CancellationToken cancellationToken = default) => await _pauseGate.ResumeAsync();

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return ValueTask.CompletedTask;
        }

        _pauseGate.Release();
        _semaphore.Dispose();
        return _consumerClient?.DisposeAsync() ?? ValueTask.CompletedTask;
    }
}
