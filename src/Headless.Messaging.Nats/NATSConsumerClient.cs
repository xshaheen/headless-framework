// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Messaging.Transport;
using Microsoft.Extensions.Options;
using NATS.Client;
using NATS.Client.JetStream;

namespace Headless.Messaging.Nats;

internal sealed class NatsConsumerClient(
    string name,
    byte groupConcurrent,
    IOptions<MessagingNatsOptions> options,
    IServiceProvider serviceProvider
) : IConsumerClient
{
    private readonly Lock _connectionLock = new();

    private readonly MessagingNatsOptions _natsOptions =
        options.Value ?? throw new ArgumentNullException(nameof(options));
    private readonly SemaphoreSlim _semaphore = new(groupConcurrent);
    private IConnection? _consumerClient;

    public Func<TransportMessage, object?, Task>? OnMessageCallback { get; set; }

    public Action<LogMessageEventArgs>? OnLogCallback { get; set; }

    public BrokerAddress BrokerAddress => new("nats", _natsOptions.Servers);

    public Task<ICollection<string>> FetchTopicsAsync(IEnumerable<string> topicNames)
    {
        if (_natsOptions.EnableSubscriberClientStreamAndSubjectCreation)
        {
            Connect();

            var jsm = _consumerClient!.CreateJetStreamManagementContext();

            var streamSubjectsGroups = topicNames.GroupBy(
                x => _natsOptions.NormalizeStreamName(x),
                StringComparer.Ordinal
            );

            foreach (var streamSubjectsGroup in streamSubjectsGroups)
            {
                var builder = StreamConfiguration
                    .Builder()
                    .WithName(streamSubjectsGroup.Key)
                    .WithNoAck(false)
                    .WithStorageType(StorageType.Memory)
                    .WithSubjects(streamSubjectsGroup.ToList());

                _natsOptions.StreamOptions?.Invoke(builder);

                try
                {
                    jsm.GetStreamInfo(streamSubjectsGroup.Key); // this throws if the stream does not exist

                    jsm.UpdateStream(builder.Build());
                }
                catch (NATSJetStreamException)
                {
                    try
                    {
                        jsm.AddStream(builder.Build());
                    }
#pragma warning disable ERP022
                    catch
                    {
                        // ignored
                    }
#pragma warning restore ERP022
                }
            }
        }

        return Task.FromResult<ICollection<string>>(topicNames.ToList());
    }

    public ValueTask SubscribeAsync(IEnumerable<string> topics)
    {
        Argument.IsNotNull(topics);

        Connect();

        var js = _consumerClient!.CreateJetStreamContext();
        var streamGroup = topics.GroupBy(x => _natsOptions.NormalizeStreamName(x), StringComparer.Ordinal);

        lock (_connectionLock)
        {
            foreach (var subjectStream in streamGroup)
            {
                var groupName = Helper.Normalized(name);

                foreach (var subject in subjectStream)
                {
                    try
                    {
                        var consumerConfig = ConsumerConfiguration
                            .Builder()
                            .WithDurable(Helper.Normalized(groupName + "-" + subject))
                            .WithDeliverPolicy(DeliverPolicy.New)
                            .WithAckWait(30000)
                            .WithAckPolicy(AckPolicy.Explicit);

                        _natsOptions.ConsumerOptions?.Invoke(consumerConfig);

                        var pso = PushSubscribeOptions
                            .Builder()
                            .WithStream(subjectStream.Key)
                            .WithConfiguration(consumerConfig.Build())
                            .Build();

                        js.PushSubscribeAsync(subject, groupName, _SubscriptionMessageHandler, false, pso);
                    }
#pragma warning disable ERP022
                    catch (Exception e)
                    {
                        OnLogCallback!(
                            new LogMessageEventArgs
                            {
                                LogType = MqLogType.ConnectError,
                                Reason =
                                    $"An error was encountered when attempting to subscribe to subject: {subject}.{Environment.NewLine}"
                                    + e,
                            }
                        );
                    }
#pragma warning restore ERP022
                }
            }
        }
        return ValueTask.CompletedTask;
    }

    public ValueTask ListeningAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            cancellationToken.WaitHandle.WaitOne(timeout);
        }
    }

#pragma warning disable VSTHRD100
    private async void _SubscriptionMessageHandler(object? sender, MsgHandlerEventArgs e)
#pragma warning restore VSTHRD100
    {
        try
        {
            if (groupConcurrent > 0)
            {
                await _semaphore.WaitAsync();
                _ = Task.Run(consumeAsync)
                    .ContinueWith(
                        static (t, state) =>
                        {
                            ((Action<LogMessageEventArgs>?)state)?.Invoke(
                                new LogMessageEventArgs
                                {
                                    LogType = MqLogType.ExceptionReceived,
                                    Reason = $"Unhandled exception in message handler: {t.Exception}",
                                }
                            );
                        },
                        OnLogCallback,
                        CancellationToken.None,
                        TaskContinuationOptions.OnlyOnFaulted,
                        TaskScheduler.Default
                    );
            }
            else
            {
                await consumeAsync();
            }
        }
        catch (Exception ex)
        {
            OnLogCallback?.Invoke(
                new LogMessageEventArgs
                {
                    LogType = MqLogType.ExceptionReceived,
                    Reason = $"Unhandled exception in message handler: {ex}",
                }
            );
        }

        async Task consumeAsync()
        {
            try
            {
                var headers = new Dictionary<string, string?>(StringComparer.Ordinal);

                foreach (string h in e.Message.Header.Keys)
                {
                    headers.Add(h, e.Message.Header[h]);
                }

                headers[Headers.Group] = name;

                if (_natsOptions.CustomHeadersBuilder != null)
                {
                    var customHeaders = _natsOptions.CustomHeadersBuilder(e, serviceProvider);
                    foreach (var customHeader in customHeaders)
                    {
                        headers[customHeader.Key] = customHeader.Value;
                    }
                }

                await OnMessageCallback!(new TransportMessage(headers, e.Message.Data), e.Message);
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
    }

    public ValueTask CommitAsync(object? sender)
    {
        if (sender is Msg msg)
        {
            msg.Ack();
        }
        _semaphore.Release();
        return ValueTask.CompletedTask;
    }

    public ValueTask RejectAsync(object? sender)
    {
        if (sender is Msg msg)
        {
            msg.Nak();
        }
        _semaphore.Release();
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _consumerClient?.Dispose();
        _semaphore.Dispose();
        return ValueTask.CompletedTask;
    }

    public void Connect()
    {
        if (_consumerClient != null)
        {
            return;
        }

        lock (_connectionLock)
        {
#pragma warning disable CA1508 // Justification: other thread can initialize it
            if (_consumerClient is null)
#pragma warning restore CA1508
            {
                var opts = _natsOptions.Options ?? ConnectionFactory.GetDefaultOptions();
                opts.Url ??= _natsOptions.Servers;
                opts.DisconnectedEventHandler = _DisconnectedEventHandler;
                opts.AsyncErrorEventHandler = _AsyncErrorEventHandler;
                opts.Timeout = 5000;
                opts.AllowReconnect = false;
                opts.NoEcho = true;

                _consumerClient = new ConnectionFactory().CreateConnection(opts);
            }
        }
    }

    private void _DisconnectedEventHandler(object? sender, ConnEventArgs e)
    {
        if (e.Error is null)
        {
            return;
        }

        var logArgs = new LogMessageEventArgs { LogType = MqLogType.ConnectError, Reason = e.Error.ToString() };
        OnLogCallback!(logArgs);
    }

    private void _AsyncErrorEventHandler(object? sender, ErrEventArgs e)
    {
        var logArgs = new LogMessageEventArgs { LogType = MqLogType.AsyncErrorEvent, Reason = e.Error };
        OnLogCallback!(logArgs);
    }
}
