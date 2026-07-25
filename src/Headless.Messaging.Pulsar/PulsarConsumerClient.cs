// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Headless.Checks;
using Headless.Messaging.Transport;
using Microsoft.Extensions.Options;
using Pulsar.Client.Api;
using Pulsar.Client.Common;

namespace Headless.Messaging.Pulsar;

internal sealed class PulsarConsumerClient(
    IOptions<PulsarMessagingOptions> options,
    PulsarClient client,
    string groupName,
    byte groupConcurrent,
    IntentType intentType = IntentType.Bus,
    TimeProvider? timeProvider = null
) : IConsumerClient
{
    private readonly SemaphoreSlim _semaphore = new(groupConcurrent);
    private readonly ConsumerPauseGate _pauseGate = new();
#pragma warning disable CA2213 // Disposing a transition gate can race queued pause/resume callers during shutdown.
    private readonly SemaphoreSlim _pauseResumeLock = new(1, 1);
#pragma warning restore CA2213
    private readonly Lock _receiveLock = new();
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private int _disposed;
    private CancellationTokenSource? _receiveCts = new();
    private readonly PulsarMessagingOptions _pulsarOptions = options.Value;
    private IConsumer<byte[]>? _consumerClient;

    public Func<TransportMessage, object?, Task>? OnMessageCallback { get; set; }

    public Action<LogMessageEventArgs>? OnLogCallback { get; set; }

    public void AttachCallbacks(Func<TransportMessage, object?, Task>? onMessage, Action<LogMessageEventArgs>? onLog)
    {
        OnMessageCallback = onMessage;
        OnLogCallback = onLog;
    }

    public BrokerAddress BrokerAddress => new("pulsar", BrokerAddressDisplay.Format(_pulsarOptions.ServiceUrl));

    public async ValueTask SubscribeAsync(IEnumerable<string> topics, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNull(topics);

        var serviceName = Assembly.GetEntryAssembly()?.GetName().Name!.ToLowerInvariant();

        // Pulsar.Client's SubscribeAsync lacks CancellationToken — use WaitAsync as a
        // timeout guard. Plan to migrate to DotPulsar (apache/pulsar-dotnet) when producer
        // batching ships: https://github.com/apache/pulsar-dotpulsar/issues/7
        var cts = TimeSpan.FromSeconds(30).ToCancellationTokenSource(cancellationToken);
        var subscribeTask = client
            .NewConsumer()
            .Topics(topics)
            .SubscriptionName(GetSubscriptionName(groupName, intentType))
            .ConsumerName(serviceName)
            .SubscriptionType(SubscriptionType.Shared)
            .NegativeAckRedeliveryDelay(_pulsarOptions.NegativeAckRedeliveryDelay)
            .SubscribeAsync();

        try
        {
            _consumerClient = await subscribeTask.WaitAsync(cts.Token).ConfigureAwait(false);
            cts.Dispose();
        }
        catch
        {
#pragma warning disable CA2025 // The cleanup task owns both the SDK task and linked CTS until completion.
            _DisposeWhenCompletedAsync(subscribeTask, cts).Forget();
#pragma warning restore CA2025
            throw;
        }

        _ready.TrySetResult();
    }

    private static async Task _DisposeWhenCompletedAsync(
        Task<IConsumer<byte[]>> consumerTask,
        CancellationTokenSource cancellationTokenSource
    )
    {
        try
        {
#pragma warning disable VSTHRD003 // Cleanup intentionally observes an SDK task started by SubscribeAsync.
            var consumer = await consumerTask.ConfigureAwait(false);
#pragma warning restore VSTHRD003
            await consumer.DisposeAsync().ConfigureAwait(false);
        }
#pragma warning disable ERP022 // Best-effort cleanup for an SDK operation abandoned by caller cancellation.
        catch
        {
            // ignored
        }
#pragma warning restore ERP022
        finally
        {
            cancellationTokenSource.Dispose();
        }
    }

    internal static string GetSubscriptionName(string groupName, IntentType intentType)
    {
        return intentType == IntentType.Queue ? "headless-queue" : groupName;
    }

    public ValueTask WaitUntilReadyAsync(CancellationToken cancellationToken = default)
    {
        return new ValueTask(_ready.Task.WaitAsync(cancellationToken));
    }

    public async ValueTask ListeningAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        var retryDelay = TimeSpan.FromMilliseconds(200);
        CancellationTokenSource? linkedReceiveCts = null;
        var receiveToken = CancellationToken.None;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                Message<byte[]> consumerResult;
                try
                {
                    await _pauseGate.WaitIfPausedAsync(cancellationToken).ConfigureAwait(false);
                    CancellationToken currentReceiveToken;
                    lock (_receiveLock)
                    {
                        if (_receiveCts is null)
                        {
                            break;
                        }

                        currentReceiveToken = _receiveCts.Token;
                    }

                    if (currentReceiveToken != receiveToken)
                    {
                        linkedReceiveCts?.Dispose();
                        linkedReceiveCts = CancellationTokenSource.CreateLinkedTokenSource(
                            cancellationToken,
                            currentReceiveToken
                        );
                        receiveToken = currentReceiveToken;
                    }

                    consumerResult = await _consumerClient!.ReceiveAsync(linkedReceiveCts!.Token).ConfigureAwait(false);
                    retryDelay = TimeSpan.FromMilliseconds(200);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (OperationCanceledException) when (receiveToken.IsCancellationRequested)
                {
                    if (Volatile.Read(ref _disposed) != 0)
                    {
                        break;
                    }

                    continue;
                }
                catch (Exception e)
                {
                    OnLogCallback!(new LogMessageEventArgs { LogType = MqLogType.ConsumeError, Reason = e.Message });
                    retryDelay = _NextBackoff(retryDelay);
                    await _timeProvider.Delay(retryDelay, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                if (groupConcurrent > 0)
                {
                    try
                    {
                        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception e) when (e is ObjectDisposedException or OperationCanceledException)
                    {
                        // Shutdown raced the concurrency-gate wait: settle the in-flight message
                        // best-effort so it is redelivered promptly, then stop. Mirrors the
                        // must-complete nack for malformed messages below and the ObjectDisposedException
                        // guard already on _semaphore.Release() in _ReleaseSemaphore.
                        await _SafeShutdownRejectAsync(consumerResult.MessageId).ConfigureAwait(false);
                        break;
                    }

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

                        // Settlement is must-complete: nack the malformed message regardless of shutdown.
                        await RejectAsync(currentMessage.MessageId, CancellationToken.None).ConfigureAwait(false);
                        return;
                    }

                    await OnMessageCallback!(message, currentMessage.MessageId).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            linkedReceiveCts?.Dispose();
        }
    }

    public async ValueTask CommitAsync(object? sender, CancellationToken cancellationToken = default)
    {
        await _consumerClient!.AcknowledgeAsync((MessageId)sender!).ConfigureAwait(false);
    }

    public async ValueTask RejectAsync(object? sender, CancellationToken cancellationToken = default)
    {
        if (sender is MessageId id)
        {
            await _consumerClient!.NegativeAcknowledge(id).ConfigureAwait(false);
        }
    }

    private async Task _SafeShutdownRejectAsync(MessageId messageId)
    {
        try
        {
            await RejectAsync(messageId, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception e) when (e is ObjectDisposedException or OperationCanceledException)
        {
            // Consumer already disposed during shutdown; Pulsar redelivers the unacknowledged message.
            OnLogCallback?.Invoke(
                new LogMessageEventArgs
                {
                    LogType = MqLogType.ConsumeError,
                    Reason = $"Best-effort shutdown nack skipped (consumer disposed): {e.Message}",
                }
            );
        }
    }

    private static TimeSpan _NextBackoff(TimeSpan current)
    {
        var floor = TimeSpan.FromMilliseconds(200);
        var ceiling = TimeSpan.FromSeconds(30);
        var doubled = TimeSpan.FromTicks(Math.Max(current.Ticks * 2, floor.Ticks));
        var capped = doubled > ceiling ? ceiling : doubled;
#pragma warning disable CA5394 // Non-security jitter for retry backoff; cryptographic RNG is unnecessary here.
        var jitterMs = Random.Shared.Next(0, (int)Math.Max(1, capped.TotalMilliseconds / 4));
#pragma warning restore CA5394
        return capped + TimeSpan.FromMilliseconds(jitterMs);
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
            catch (ObjectDisposedException)
            {
                // Shutdown in progress — semaphore already disposed.
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

    public async ValueTask PauseAsync(CancellationToken cancellationToken = default)
    {
        await _pauseResumeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref _disposed) != 0 || !await _pauseGate.PauseAsync().ConfigureAwait(false))
            {
                return;
            }

            lock (_receiveLock)
            {
                if (Volatile.Read(ref _disposed) != 0 || _receiveCts is null)
                {
                    return;
                }

#pragma warning disable CA1849, VSTHRD103 // Cancellation must stay under the lock so disposal cannot win the race.
                _receiveCts.Cancel();
#pragma warning restore CA1849, VSTHRD103
            }
        }
        finally
        {
            _pauseResumeLock.Release();
        }
    }

    public async ValueTask ResumeAsync(CancellationToken cancellationToken = default)
    {
        await _pauseResumeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref _disposed) != 0 || !_pauseGate.IsPaused)
            {
                return;
            }

            CancellationTokenSource previousReceiveCts;
            lock (_receiveLock)
            {
                if (Volatile.Read(ref _disposed) != 0 || _receiveCts is null)
                {
                    return;
                }

                previousReceiveCts = _receiveCts;
                _receiveCts = new CancellationTokenSource();
            }

            previousReceiveCts.Dispose();
            await _pauseGate.ResumeAsync().ConfigureAwait(false);
        }
        finally
        {
            _pauseResumeLock.Release();
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
        CancellationTokenSource? receiveCts;
        lock (_receiveLock)
        {
            receiveCts = _receiveCts;
            _receiveCts = null;
#pragma warning disable CA1849, VSTHRD103 // Cancellation must stay under the lock so no pause operation observes a disposed source.
            receiveCts?.Cancel();
#pragma warning restore CA1849, VSTHRD103
        }

        receiveCts?.Dispose();
        _semaphore.Dispose();
        if (_consumerClient is not null)
        {
            await _consumerClient.DisposeAsync().ConfigureAwait(false);
        }
    }
}
