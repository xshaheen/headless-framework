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
    IServiceProvider serviceProvider
) : IConsumerClient
{
    private readonly MessagingNatsOptions _natsOptions =
        options.Value ?? throw new ArgumentNullException(nameof(options));

    private readonly SemaphoreSlim _semaphore = new(groupConcurrent);
    private readonly ConsumerPauseGate _pauseGate = new();

    private NatsConnection? _connection;
    private NatsJSContext? _jsContext;
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

        var streamSubjectsGroups = topicNames.GroupBy(x => _natsOptions.NormalizeStreamName(x), StringComparer.Ordinal);

        foreach (var streamSubjectsGroup in streamSubjectsGroups)
        {
            var config = new StreamConfig
            {
                Name = streamSubjectsGroup.Key,
                Subjects = streamSubjectsGroup.ToList(),
                NoAck = false,
                Storage = StreamConfigStorage.Memory,
            };

            _natsOptions.StreamOptions?.Invoke(config);

            try
            {
                await _jsContext!.UpdateStreamAsync(config).ConfigureAwait(false);
            }
            catch (NatsJSApiException ex) when (ex.Error.Code == 404)
            {
                try
                {
                    await _jsContext!.CreateStreamAsync(config).ConfigureAwait(false);
                }
                catch (NatsJSApiException e) when (e.Error.Code == 409)
                {
                    // Stream was created by another instance between check and create
                }
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

                tasks.Add(_ConsumeSubjectAsync(streamGroup.Key, consumerConfig, cancellationToken));
            }
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private async Task _ConsumeSubjectAsync(
        string streamName,
        ConsumerConfig consumerConfig,
        CancellationToken cancellationToken
    )
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var consumer = await _jsContext!
                    .CreateOrUpdateConsumerAsync(streamName, consumerConfig, cancellationToken)
                    .ConfigureAwait(false);

                await foreach (
                    var msg in consumer
                        .ConsumeAsync<ReadOnlyMemory<byte>>(
                            serializer: NatsRawSerializer<ReadOnlyMemory<byte>>.Default,
                            cancellationToken: cancellationToken
                        )
                        .ConfigureAwait(false)
                )
                {
                    await _pauseGate.WaitIfPausedAsync(cancellationToken).ConfigureAwait(false);

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
                        await _ProcessMessageAsync(msg).ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (NatsJSApiException ex)
            {
                OnLogCallback?.Invoke(
                    new LogMessageEventArgs
                    {
                        LogType = MqLogType.ConnectError,
                        Reason = $"JetStream API error for stream '{streamName}', will retry: {ex}",
                    }
                );

                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                OnLogCallback?.Invoke(
                    new LogMessageEventArgs
                    {
                        LogType = MqLogType.ExceptionReceived,
                        Reason = $"Consumer error for stream '{streamName}', will retry: {ex}",
                    }
                );

                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task _ProcessMessageAsync(INatsJSMsg<ReadOnlyMemory<byte>> msg)
    {
        try
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

            if (OnMessageCallback is not null)
            {
                // Box the struct for the sender parameter — used by CommitAsync/RejectAsync
                await OnMessageCallback(new TransportMessage(headers, msg.Data), msg).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            OnLogCallback?.Invoke(
                new LogMessageEventArgs
                {
                    LogType = MqLogType.ExceptionReceived,
                    Reason = $"Unhandled exception processing message: {ex}",
                }
            );
        }
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
        await _pauseGate.PauseAsync();
    }

    public async ValueTask ResumeAsync(CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref _disposed) != 0)
            return;
        await _pauseGate.ResumeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _pauseGate.Release();
        _semaphore.Dispose();

        if (_connection is not null)
        {
            await _connection.DisposeAsync().ConfigureAwait(false);
        }
    }
}
