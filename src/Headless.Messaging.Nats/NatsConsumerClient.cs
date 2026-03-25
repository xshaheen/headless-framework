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

    private readonly SemaphoreSlim? _semaphore = groupConcurrent > 0 ? new SemaphoreSlim(groupConcurrent) : null;
    private readonly ConsumerPauseGate _pauseGate = new();
    private readonly Func<string, ConsumerConfig, CancellationToken, Task<INatsJSConsumer>>? _consumerFactory =
        consumerFactory;

    private readonly List<CancellationTokenSource> _oldReceiveCts = [];
    private NatsConnection? _connection;
    private NatsJSContext? _jsContext;
    private CancellationTokenSource _receiveCts = new();
    private IEnumerable<string>? _subscribedTopics;
    private int _disposed;

    public Func<TransportMessage, object?, Task>? OnMessageCallback { get; set; }

    public Action<LogMessageEventArgs>? OnLogCallback { get; set; }

    public BrokerAddress BrokerAddress => new("nats", _natsOptions.GetSanitizedServersForDisplay());

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

        // Preserve wildcard coverage for hierarchical subjects, but add exact
        // subjects for bare/non-prefix topics that the wildcard cannot match.
        var streamGroups = topicNames.GroupBy(x => _natsOptions.NormalizeStreamName(x), StringComparer.Ordinal);

        foreach (var streamGroup in streamGroups)
        {
            var config = new StreamConfig
            {
                Name = streamGroup.Key,
                Subjects = [.. BuildStreamSubjects(streamGroup.Key, streamGroup)],
                NoAck = false,
                Storage = StreamConfigStorage.File,
            };

            _natsOptions.StreamOptions?.Invoke(config);

            await _jsContext!.CreateOrUpdateStreamAsync(config).ConfigureAwait(false);
        }

        return topicNames.ToList();
    }

    internal static IReadOnlyList<string> BuildStreamSubjects(string streamName, IEnumerable<string> topicNames)
    {
        Argument.IsNotNull(topicNames);

        var subjects = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var hierarchicalPrefix = streamName + ".";
        var hasHierarchicalSubjects = false;

        foreach (var topicName in topicNames)
        {
            if (!seen.Add(topicName))
            {
                continue;
            }

            if (topicName.StartsWith(hierarchicalPrefix, StringComparison.Ordinal))
            {
                hasHierarchicalSubjects = true;
                continue;
            }

            subjects.Add(topicName);
        }

        if (hasHierarchicalSubjects)
        {
            subjects.Add($"{streamName}.>");
        }

        return subjects;
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
        var retryDelay = TimeSpan.FromSeconds(1);
        var nextOpts = timeout > TimeSpan.Zero ? new NatsJSNextOpts { Expires = timeout } : (NatsJSNextOpts?)null;

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
                            opts: nextOpts,
                            cancellationToken: receiveCts.Token
                        )
                        .ConfigureAwait(false);

                    if (msg is null)
                    {
                        continue;
                    }

                    retryDelay = TimeSpan.FromSeconds(1);
                    await _DispatchMessageAsync(msg, cancellationToken).ConfigureAwait(false);
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
                OnLogCallback?.Invoke(
                    new LogMessageEventArgs
                    {
                        LogType = MqLogType.ConnectError,
                        Reason = $"JetStream API error for stream '{streamName}', will retry: {ex}",
                    }
                );

                // API errors use a 5s floor before escalating
                retryDelay = _NextBackoff(retryDelay, floor: TimeSpan.FromSeconds(5));
                await Task.Delay(retryDelay, cancellationToken).ConfigureAwait(false);
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

                retryDelay = _NextBackoff(retryDelay);
                await Task.Delay(retryDelay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async ValueTask _DispatchMessageAsync(
        INatsJSMsg<ReadOnlyMemory<byte>> msg,
        CancellationToken cancellationToken
    )
    {
        if (_semaphore is not null)
        {
            await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

            _ = RunConcurrentHandlerIgnoringCancellation(
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

    internal static Task RunConcurrentHandlerIgnoringCancellation(
        Func<Task> handler,
        CancellationToken cancellationToken
    )
    {
        _ = cancellationToken;

        // Scheduling must ignore cancellation so the release finally always runs.
        return Task.Run(handler);
    }

    private static TimeSpan _NextBackoff(TimeSpan current, TimeSpan floor = default)
    {
        var ceiling = TimeSpan.FromSeconds(30);
        var next = TimeSpan.FromTicks(Math.Min(current.Ticks * 2, ceiling.Ticks));
        return floor > next ? floor : next;
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
        if (_semaphore is null)
            return;

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
        foreach (var cts in _oldReceiveCts)
            cts.Dispose();
        _oldReceiveCts.Clear();
        _semaphore?.Dispose();

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
        lock (_receiveLock)
        {
            // Don't dispose previous CTS eagerly — the consume loop may still hold
            // a token from it inside CreateLinkedTokenSource. Defer to DisposeAsync.
            _oldReceiveCts.Add(_receiveCts);
            _receiveCts = new CancellationTokenSource();
        }
    }
}
