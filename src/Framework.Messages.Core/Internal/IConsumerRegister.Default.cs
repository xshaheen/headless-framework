// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Framework.Messages.Configuration;
using Framework.Messages.Diagnostics;
using Framework.Messages.Exceptions;
using Framework.Messages.Messages;
using Framework.Messages.Persistence;
using Framework.Messages.Serialization;
using Framework.Messages.Transport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Framework.Messages.Internal;

internal class ConsumerRegister(ILogger<ConsumerRegister> logger, IServiceProvider serviceProvider) : IConsumerRegister
{
    // diagnostics listener
    // ReSharper disable once InconsistentNaming
    private static readonly DiagnosticListener s_diagnosticListener = new(
        MessageDiagnosticListenerNames.DiagnosticListenerName
    );

    private readonly ILogger _logger = logger;
    private readonly MessagingOptions _options = serviceProvider.GetRequiredService<IOptions<MessagingOptions>>().Value;
    private readonly TimeSpan _pollingDelay = TimeSpan.FromSeconds(1);
    private Task? _compositeTask;

    private IConsumerClientFactory _consumerClientFactory = default!;
    private CancellationTokenSource _cts = new();
    private IDispatcher _dispatcher = default!;
    private int _disposed;
    private bool _isHealthy = true;

    private MethodMatcherCache _selector = default!;
    private ISerializer _serializer = default!;
    private BrokerAddress _serverAddress;
    private IDataStorage _storage = default!;

    public bool IsHealthy()
    {
        return _isHealthy;
    }

    public async ValueTask StartAsync(CancellationToken stoppingToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        _cts.Token.Register(Dispose);

        _selector = serviceProvider.GetRequiredService<MethodMatcherCache>();
        _dispatcher = serviceProvider.GetRequiredService<IDispatcher>();
        _serializer = serviceProvider.GetRequiredService<ISerializer>();
        _storage = serviceProvider.GetRequiredService<IDataStorage>();
        _consumerClientFactory = serviceProvider.GetRequiredService<IConsumerClientFactory>();

        await ExecuteAsync();

        _disposed = 0;
    }

    public async ValueTask ReStartAsync(bool force = false)
    {
        if (!IsHealthy() || force)
        {
            Pulse();

            _cts = new CancellationTokenSource();
            _isHealthy = true;

            await ExecuteAsync();
        }
    }

    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) == 1)
        {
            return;
        }

        try
        {
            Pulse();

            _compositeTask?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException ex)
        {
            var innerEx = ex.InnerExceptions[0];
            if (!(innerEx is OperationCanceledException))
            {
                _logger.ExpectedOperationCanceledException(innerEx);
            }
        }
    }

    public void Pulse()
    {
        _cts.Cancel();
        _cts.Dispose();
    }

    public async ValueTask ExecuteAsync()
    {
        var groupingMatches = _selector.GetCandidatesMethodsOfGroupNameGrouped();

        foreach (var matchGroup in groupingMatches)
        {
            ICollection<string> topics;
            var limit = _selector.GetGroupConcurrentLimit(matchGroup.Key);
            try
            {
                await using var client = await _consumerClientFactory.CreateAsync(matchGroup.Key, limit);
                client.OnLogCallback = _WriteLog;
                topics = await client.FetchTopicsAsync(matchGroup.Value.Select(x => x.TopicName));
            }
            catch (BrokerConnectionException e)
            {
                _isHealthy = false;
                _logger.LogError(e, e.Message);
                return;
            }

            for (var i = 0; i < _options.ConsumerThreadCount; i++)
            {
                var topicIds = topics.Select(t => t);
                _ = Task.Factory.StartNew(
                    async () =>
                    {
                        try
                        {
                            await using var client = await _consumerClientFactory.CreateAsync(matchGroup.Key, limit);

                            _serverAddress = client.BrokerAddress;

                            _RegisterMessageProcessor(client);

                            await client.SubscribeAsync(topicIds);

                            await client.ListeningAsync(_pollingDelay, _cts.Token);
                        }
                        catch (OperationCanceledException)
                        {
                            //ignore
                        }
                        catch (BrokerConnectionException e)
                        {
                            _isHealthy = false;
                            _logger.LogError(e, e.Message);
                        }
                        catch (Exception e)
                        {
                            _logger.LogError(e, e.Message);
                        }
                    },
                    _cts.Token,
                    TaskCreationOptions.LongRunning,
                    TaskScheduler.Default
                );
            }
        }

        _compositeTask = Task.CompletedTask;
    }

    private void _RegisterMessageProcessor(IConsumerClient client)
    {
        client.OnLogCallback = _WriteLog;
        client.OnMessageCallback = async (transportMessage, sender) =>
        {
            long? tracingTimestamp = null;
            try
            {
                _logger.MessageReceived(transportMessage.GetId(), transportMessage.GetName());

                tracingTimestamp = _TracingBefore(transportMessage, _serverAddress);

                var name = transportMessage.GetName();
                var group = transportMessage.GetGroup()!;

                Message message;

                var canFindSubscriber = _selector.TryGetTopicExecutor(name, group, out var executor);
                try
                {
                    if (!canFindSubscriber)
                    {
                        var error =
                            $"Message can not be found subscriber. Name:{name}, Group:{group}. {Environment.NewLine} see: https://github.com/dotnetcore/CAP/issues/63";
                        var ex = new SubscriberNotFoundException(error);

                        _TracingError(tracingTimestamp, transportMessage, client.BrokerAddress, ex);

                        throw ex;
                    }

                    var type = executor!.Parameters.FirstOrDefault(x => x.IsFromCap == false)?.ParameterType;
                    message = await _serializer.DeserializeAsync(transportMessage, type);
                    message.RemoveException();
                }
                catch (Exception e)
                {
                    transportMessage.Headers[Headers.Exception] = e.GetType().Name + "-->" + e.Message;
                    string? dataUri;
                    if (transportMessage.Headers.TryGetValue(Headers.Type, out var val))
                    {
                        if (transportMessage.Body.Length != 0)
                        {
                            dataUri = $"data:{val};base64," + Convert.ToBase64String(transportMessage.Body.Span);
                        }
                        else
                        {
                            dataUri = null;
                        }

                        message = new Message(transportMessage.Headers, dataUri);
                    }
                    else
                    {
                        if (transportMessage.Body.Length != 0)
                        {
                            dataUri = "data:UnknownType;base64," + Convert.ToBase64String(transportMessage.Body.Span);
                        }
                        else
                        {
                            dataUri = null;
                        }

                        message = new Message(transportMessage.Headers, dataUri);
                    }
                }

                if (message.HasException())
                {
#pragma warning disable CA1849, VSTHRD103
                    var content = _serializer.Serialize(message);
#pragma warning restore VSTHRD103, CA1849

                    await _storage.StoreReceivedExceptionMessageAsync(name, group, content);

                    await client.CommitAsync(sender);

                    try
                    {
                        _options.FailedThresholdCallback?.Invoke(
                            new FailedInfo
                            {
                                ServiceProvider = serviceProvider,
                                MessageType = MessageType.Subscribe,
                                Message = message,
                            }
                        );

                        _logger.ConsumerExecutedAfterThreshold(message.GetId(), _options.FailedRetryCount);
                    }
                    catch (Exception e)
                    {
                        _logger.ExecutedThresholdCallbackFailed(e);
                    }

                    _TracingAfter(tracingTimestamp, transportMessage, _serverAddress);
                }
                else
                {
                    var mediumMessage = await _storage.StoreReceivedMessageAsync(name, group, message);
                    mediumMessage.Origin = message;

                    _TracingAfter(tracingTimestamp, transportMessage, _serverAddress);

                    await _dispatcher.EnqueueToExecute(mediumMessage, executor!);

                    await client.CommitAsync(sender);
                }
            }
            catch (Exception e)
            {
                _logger.LogError(
                    e,
                    "An exception occurred when process received message. Message:'{0}'.",
                    transportMessage
                );

                await client.RejectAsync(sender);

                _TracingError(tracingTimestamp, transportMessage, client.BrokerAddress, e);
            }
        };
    }

    private void _WriteLog(LogMessageEventArgs logmsg)
    {
        switch (logmsg.LogType)
        {
            case MqLogType.ConsumerCancelled:
                _isHealthy = false;
                _logger.LogWarning("RabbitMQ consumer cancelled. --> " + logmsg.Reason);
                break;
            case MqLogType.ConsumerRegistered:
                _isHealthy = true;
                _logger.LogInformation("RabbitMQ consumer registered. --> " + logmsg.Reason);
                break;
            case MqLogType.ConsumerUnregistered:
                _logger.LogWarning("RabbitMQ consumer unregistered. --> " + logmsg.Reason);
                break;
            case MqLogType.ConsumerShutdown:
                _isHealthy = false;
                _logger.LogWarning("RabbitMQ consumer shutdown. --> " + logmsg.Reason);
                break;
            case MqLogType.ConsumeError:
                _logger.LogError("Kafka client consume error. --> " + logmsg.Reason);
                break;
            case MqLogType.ConsumeRetries:
                _logger.LogWarning("Kafka client consume exception, retying... --> " + logmsg.Reason);
                break;
            case MqLogType.ServerConnError:
                _isHealthy = false;
                _logger.LogCritical("Kafka server connection error. --> " + logmsg.Reason);
                break;
            case MqLogType.ExceptionReceived:
                _logger.LogError("AzureServiceBus subscriber received an error. --> " + logmsg.Reason);
                break;
            case MqLogType.AsyncErrorEvent:
                _logger.LogError("NATS subscriber received an error. --> " + logmsg.Reason);
                break;
            case MqLogType.ConnectError:
                _isHealthy = false;
                _logger.LogError("NATS server connection error. -->  " + logmsg.Reason);
                break;
            case MqLogType.InvalidIdFormat:
                _logger.LogError(
                    "AmazonSQS subscriber delete inflight message failed, invalid id. --> " + logmsg.Reason
                );
                break;
            case MqLogType.MessageNotInflight:
                _logger.LogError(
                    "AmazonSQS subscriber change message's visibility failed, message isn't in flight. --> "
                        + logmsg.Reason
                );
                break;
            case MqLogType.RedisConsumeError:
                _isHealthy = true;
                _logger.LogError("Redis client consume error. --> {reason}", logmsg.Reason);
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    #region tracing

    private long? _TracingBefore(TransportMessage message, BrokerAddress broker)
    {
        if (s_diagnosticListener.IsEnabled(MessageDiagnosticListenerNames.BeforeConsume))
        {
            var eventData = new MessageEventDataSubStore
            {
                OperationTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Operation = message.GetName(),
                BrokerAddress = broker,
                TransportMessage = message,
            };

            s_diagnosticListener.Write(MessageDiagnosticListenerNames.BeforeConsume, eventData);

            return eventData.OperationTimestamp;
        }

        return null;
    }

    private void _TracingAfter(long? tracingTimestamp, TransportMessage message, BrokerAddress broker)
    {
        MessageEventCounterSource.Log.WriteConsumeMetrics();
        if (tracingTimestamp != null && s_diagnosticListener.IsEnabled(MessageDiagnosticListenerNames.AfterConsume))
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var eventData = new MessageEventDataSubStore
            {
                OperationTimestamp = now,
                Operation = message.GetName(),
                BrokerAddress = broker,
                TransportMessage = message,
                ElapsedTimeMs = now - tracingTimestamp.Value,
            };

            s_diagnosticListener.Write(MessageDiagnosticListenerNames.AfterConsume, eventData);
        }
    }

    private void _TracingError(long? tracingTimestamp, TransportMessage message, BrokerAddress broker, Exception ex)
    {
        if (tracingTimestamp != null && s_diagnosticListener.IsEnabled(MessageDiagnosticListenerNames.ErrorConsume))
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            var eventData = new MessageEventDataSubStore
            {
                OperationTimestamp = now,
                Operation = message.GetName(),
                BrokerAddress = broker,
                TransportMessage = message,
                ElapsedTimeMs = now - tracingTimestamp.Value,
                Exception = ex,
            };

            s_diagnosticListener.Write(MessageDiagnosticListenerNames.ErrorConsume, eventData);
        }
    }

    #endregion
}
