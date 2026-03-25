// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Messaging.Transport;
using Microsoft.Extensions.Options;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;

namespace Headless.Messaging.Nats;

internal sealed class NatsConsumerClient(
    string name,
    byte groupConcurrent,
    IOptions<MessagingNatsOptions> options,
    IServiceProvider serviceProvider,
    Func<string, ConsumerConfig, CancellationToken, Task<INatsJSConsumer>>? consumerFactory = null
) : IConsumerClient
{
    private readonly Lock _receiveLock = new();
    private readonly MessagingNatsOptions _natsOptions =
        options.Value ?? throw new ArgumentNullException(nameof(options));

    private readonly SemaphoreSlim _semaphore = new(groupConcurrent);
    private readonly ConsumerPauseGate _pauseGate = new();
    private readonly Func<string, ConsumerConfig, CancellationToken, Task<INatsJSConsumer>>? _consumerFactory =
        consumerFactory;

    private NatsConnection? _connection;
    private NatsJSContext? _jsContext;
    private CancellationTokenSource _receiveCts = new();
    private IEnumerable<string>? _subscribedTopics;
    private int _disposed;

    public Func<TransportMessage, object?, Task>? OnMessageCallback { get; set; }

    public Action<LogMessageEventArgs>? OnLogCallback { get; set; }

    public BrokerAddress BrokerAddress => new("nats", _natsOptions.Servers);

    public async Task ConnectAsync()
    {
        var opts = _natsOptions.BuildNatsOpts();
        _connection = new NatsConnection(opts);
        await _connection.ConnectAsync().ConfigureAwait(false);
        _jsContext = new NatsJSContext(_connection);
    }

    public async ValueTask<ICollection<string>> FetchTopicsAsync(IEnumerable<string> topicNames)
    {
        if (!_natsOptions.EnableSubscriberClientStreamAndSubjectCreation)
        {
            return topicNames.ToList();
        }

        // Group topics by stream name, then create each stream with a wildcard
        // subject (e.g., "orders.>") instead of explicit subject lists. This is
        // multi-instance safe: all instances create the same stream with the same
        // wildcard, eliminating subject-overwrite races. Individual consumers use
        // FilterSubject on their ConsumerConfig for precise topic matching.
        var streamNames = topicNames.Select(x => _natsOptions.NormalizeStreamName(x)).Distinct(StringComparer.Ordinal);

        foreach (var streamName in streamNames)
        {
            var config = new StreamConfig
            {
                Name = streamName,
                Subjects = [$"{streamName}.>"],
                NoAck = false,
                Storage = StreamConfigStorage.Memory,
            };

            _natsOptions.StreamOptions?.Invoke(config);

            try
            {
                await _jsContext!.CreateStreamAsync(config).ConfigureAwait(false);
            }
            catch (NatsJSApiException e) when (e.Error.Code == 409)
            {
                // Stream already exists — safe to ignore, wildcard subject is idempotent
            }
        }

        return topicNames.ToList();
    }

    public ValueTask SubscribeAsync(IEnumerable<string> topics)
    {
        Argument.IsNotNull(topics);

        _subscribedTopics = topics.ToList();

        return ValueTask.CompletedTask;
    }

    public async ValueTask ListeningAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        var streamGroups = _subscribedTopics!.GroupBy(x => _natsOptions.NormalizeStreamName(x), StringComparer.Ordinal);

        var tasks = new List<Task>();

        foreach (var streamGroup in streamGroups)
        {
            var groupName = Helper.Normalized(name);

            foreach (var subject in streamGroup)
            {
                var durableName = Helper.Normalized(groupName + "-" + subject);

                var consumerConfig = new ConsumerConfig(durableName)
                {
                    FilterSubject = subject,
                    AckWait = TimeSpan.FromSeconds(30),
                };

                _natsOptions.ConsumerOptions?.Invoke(consumerConfig);

                tasks.Add(_ConsumeSubjectAsync(streamGroup.Key, consumerConfig, timeout, cancellationToken));
            }
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private async Task _ConsumeSubjectAsync(
        string streamName,
        ConsumerConfig consumerConfig,
        TimeSpan timeout,
        CancellationToken cancellationToken
    )
    {
        // Shared across API and transient error paths — escalation carries
        // over between error types intentionally so sustained mixed failures
        // still back off. Reset on successful message receive (line below).
        var retryDelay = TimeSpan.FromSeconds(1);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var consumer = _consumerFactory is not null
                    ? await _consumerFactory(streamName, consumerConfig, cancellationToken).ConfigureAwait(false)
                    : await _jsContext!
                        .CreateOrUpdateConsumerAsync(streamName, consumerConfig, cancellationToken)
                        .ConfigureAwait(false);

                while (!cancellationToken.IsCancellationRequested)
                {
                    await _pauseGate.WaitIfPausedAsync(cancellationToken).ConfigureAwait(false);

                    using var receiveCts = CancellationTokenSource.CreateLinkedTokenSource(
                        cancellationToken,
                        _GetReceiveToken()
                    );

                    var msg = await consumer
                        .NextAsync(
                            serializer: NatsRawSerializer<ReadOnlyMemory<byte>>.Default,
                            opts: _CreateNextOptions(timeout),
                            cancellationToken: receiveCts.Token
                        )
                        .ConfigureAwait(false);

                    if (msg is null)
                    {
                        continue;
                    }

                    // Successful receive proves the consumer/stream is healthy — reset backoff
                    retryDelay = TimeSpan.FromSeconds(1);

                    if (groupConcurrent > 0)
                    {
                        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

                        _ = Task.Run(
                            async () =>
                            {
                                try
                                {
                                    await _ProcessMessageAsync(msg).ConfigureAwait(false);
                                }
                                catch (Exception ex)
                                {
                                    OnLogCallback?.Invoke(
                                        new LogMessageEventArgs
                                        {
                                            LogType = MqLogType.ExceptionReceived,
                                            Reason = $"Unhandled exception in concurrent message handler: {ex}",
                                        }
                                    );
                                }
                                finally
                                {
                                    _ReleaseSemaphore();
                                }
                            },
                            cancellationToken
                        );
                    }
                    else
                    {
                        try
                        {
                            await _ProcessMessageAsync(msg).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            OnLogCallback?.Invoke(
                                new LogMessageEventArgs
                                {
                                    LogType = MqLogType.ExceptionReceived,
                                    Reason = $"Unhandled exception in sequential message handler: {ex}",
                                }
                            );
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (OperationCanceledException) when (_pauseGate.IsPaused)
            {
                continue;
            }
            catch (NatsJSApiException ex)
            {
                // API errors (stream not ready, deleted, permissions) — longer initial backoff
                OnLogCallback?.Invoke(
                    new LogMessageEventArgs
                    {
                        LogType = MqLogType.ConnectError,
                        Reason = $"JetStream API error for stream '{streamName}', will retry: {ex}",
                    }
                );

                var apiDelay = TimeSpan.FromTicks(Math.Max(retryDelay.Ticks, TimeSpan.FromSeconds(5).Ticks));
                await Task.Delay(apiDelay, cancellationToken).ConfigureAwait(false);
                retryDelay = TimeSpan.FromTicks(Math.Min(retryDelay.Ticks * 2, TimeSpan.FromSeconds(30).Ticks));
            }
            catch (Exception ex)
            {
                // Transient errors (network, timeout) — shorter initial backoff
                OnLogCallback?.Invoke(
                    new LogMessageEventArgs
                    {
                        LogType = MqLogType.ExceptionReceived,
                        Reason = $"Consumer error for stream '{streamName}', will retry: {ex}",
                    }
                );

                await Task.Delay(retryDelay, cancellationToken).ConfigureAwait(false);
                retryDelay = TimeSpan.FromTicks(Math.Min(retryDelay.Ticks * 2, TimeSpan.FromSeconds(30).Ticks));
            }
        }
    }

    private static NatsJSNextOpts? _CreateNextOptions(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero)
        {
            return null;
        }

        return new NatsJSNextOpts { Expires = timeout };
    }

    private async Task _ProcessMessageAsync(INatsJSMsg<ReadOnlyMemory<byte>> msg)
    {
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal);

        if (msg.Headers is { Count: > 0 } natsHeaders)
        {
            foreach (var (key, values) in natsHeaders)
            {
                headers[key] = values.Count > 0 ? values[0] : null;
            }
        }

        headers[Headers.Group] = name;

        if (_natsOptions.CustomHeadersBuilder is not null)
        {
            var metadata = msg.Metadata;
            var customHeaders = _natsOptions.CustomHeadersBuilder(metadata, msg.Headers, serviceProvider);
            foreach (var customHeader in customHeaders)
            {
                headers[customHeader.Key] = customHeader.Value;
            }
        }

        // Let exceptions propagate — the framework's OnMessageCallback handler
        // calls CommitAsync on success and RejectAsync on failure.
        await OnMessageCallback!(new TransportMessage(headers, msg.Data), msg).ConfigureAwait(false);
    }

    public async ValueTask CommitAsync(object? sender)
    {
        try
        {
            if (sender is INatsJSMsg<ReadOnlyMemory<byte>> msg)
            {
                await msg.AckAsync().ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            OnLogCallback?.Invoke(
                new LogMessageEventArgs
                {
                    LogType = MqLogType.AsyncErrorEvent,
                    Reason = $"NATS message ACK failed: {ex}",
                }
            );
        }
    }

    public async ValueTask RejectAsync(object? sender)
    {
        try
        {
            if (sender is INatsJSMsg<ReadOnlyMemory<byte>> msg)
            {
                await msg.NakAsync().ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            OnLogCallback?.Invoke(
                new LogMessageEventArgs
                {
                    LogType = MqLogType.AsyncErrorEvent,
                    Reason = $"NATS message NAK failed: {ex}",
                }
            );
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
            catch (ObjectDisposedException)
            {
                // Shutdown in progress — semaphore already disposed
            }
        }
    }

    public async ValueTask PauseAsync(CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref _disposed) != 0)
            return;
        if (!await _pauseGate.PauseAsync())
            return;

        _CancelReceives();
    }

    public async ValueTask ResumeAsync(CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref _disposed) != 0)
            return;
        if (!await _pauseGate.ResumeAsync())
            return;

        _ResetReceiveToken();
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _pauseGate.Release();
        _CancelReceives();
        _receiveCts.Dispose();
        _semaphore.Dispose();

        if (_connection is not null)
        {
            await _connection.DisposeAsync().ConfigureAwait(false);
        }
    }

    private CancellationToken _GetReceiveToken()
    {
        lock (_receiveLock)
        {
            return _receiveCts.Token;
        }
    }

    private void _CancelReceives()
    {
        CancellationTokenSource receiveCts;
        lock (_receiveLock)
        {
            receiveCts = _receiveCts;
        }

        try
        {
            receiveCts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Shutdown already disposed the receive CTS.
        }
    }

    private void _ResetReceiveToken()
    {
        CancellationTokenSource previous;
        lock (_receiveLock)
        {
            previous = _receiveCts;
            _receiveCts = new CancellationTokenSource();
        }

        previous.Dispose();
    }
}
