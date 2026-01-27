// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Headless.Messaging.Configuration;
using Headless.Messaging.Diagnostics;
using Headless.Messaging.Exceptions;
using Headless.Messaging.Messages;
using Headless.Messaging.Persistence;
using Headless.Messaging.Serialization;
using Headless.Messaging.Transport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.Internal;

/// <summary>
/// Handler received message of subscribed.
/// </summary>
public interface IConsumerRegister : IProcessingServer
{
    bool IsHealthy();

    ValueTask ReStartAsync(bool force = false);
}

internal sealed class ConsumerRegister(ILogger<ConsumerRegister> logger, IServiceProvider serviceProvider)
    : IConsumerRegister
{
    private static readonly DiagnosticListener _DiagnosticListener = new(
        MessageDiagnosticListenerNames.DiagnosticListenerName
    );

    private readonly ILogger _logger = logger;
    private readonly MessagingOptions _options = serviceProvider.GetRequiredService<IOptions<MessagingOptions>>().Value;
    private readonly TimeSpan _pollingDelay = TimeSpan.FromSeconds(1);
    private Task? _compositeTask;

    private IConsumerClientFactory _consumerClientFactory = null!;
    private CancellationTokenSource _cts = new();
    private IDispatcher _dispatcher = null!;
    private int _disposed;
    private volatile bool _isHealthy = true;

    private MethodMatcherCache _selector = null!;
    private ISerializer _serializer = null!;
    private BrokerAddress _serverAddress;
    private IDataStorage _storage = null!;

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
            await PulseAsync();

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
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) == 1)
        {
            return;
        }

        try
        {
            await PulseAsync();

            if (_compositeTask is not null)
            {
                await _compositeTask.WaitAsync(TimeSpan.FromSeconds(2));
            }
        }
        catch (AggregateException e)
        {
            var innerEx = e.InnerExceptions[0];
            if (innerEx is not OperationCanceledException)
            {
                _logger.ExpectedOperationCanceledException(innerEx);
            }
        }
        finally
        {
            // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
            if (_dispatcher is not null)
            {
                await _dispatcher.DisposeAsync();
            }
        }
    }

    public async Task PulseAsync()
    {
        await _cts.CancelAsync();
        _cts.Dispose();
    }

    public async ValueTask ExecuteAsync()
    {
        var groupingMatches = _selector.GetCandidatesMethodsOfGroupNameGrouped();
        var consumerTasks = new List<Task>();

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
                _logger.LogError(e, "Failed to connect to broker. {Message}", e.Message);
                return;
            }

            for (var i = 0; i < _options.ConsumerThreadCount; i++)
            {
                var topicIds = topics.Select(t => t);
                var task = Task
                    .Factory.StartNew(
                        async () =>
                        {
                            try
                            {
                                await using var client = await _consumerClientFactory.CreateAsync(
                                    matchGroup.Key,
                                    limit
                                );

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
                                _logger.LogError(e, "Failed to connect to broker. {Message}", e.Message);
                            }
                            catch (Exception e)
                            {
                                _logger.LogError(
                                    e,
                                    "An exception occurred in consumer processing loop. {Message}",
                                    e.Message
                                );
                            }
                        },
                        _cts.Token,
                        TaskCreationOptions.LongRunning,
                        TaskScheduler.Default
                    )
                    .Unwrap();

                consumerTasks.Add(task);
            }
        }

        _compositeTask = Task.WhenAll(consumerTasks);
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
                            $"Message can not be found subscriber. Name:{name}, Group:{group}. {Environment.NewLine} Ensure the subscriber method is decorated with [Subscribe] and the consumer group matches.";
                        var ex = new SubscriberNotFoundException(error);

                        _TracingError(tracingTimestamp, transportMessage, client.BrokerAddress, ex);

                        throw ex;
                    }

                    var type = executor!.Parameters.FirstOrDefault(x => !x.IsFromMessaging)?.ParameterType;
                    message = await _serializer.DeserializeAsync(transportMessage, type);
                    message.RemoveException();
                }
                catch (Exception e)
                {
#pragma warning disable EPC12 // Suppress CA2200 warning to rethrow original exception
                    transportMessage.Headers[Headers.Exception] = e.GetType().Name + "-->" + e.Message;
#pragma warning restore EPC12

                    string? dataUri;
                    if (transportMessage.Headers.TryGetValue(Headers.Type, out var val))
                    {
                        dataUri =
                            transportMessage.Body.Length != 0
                                ? $"data:{val};base64," + Convert.ToBase64String(transportMessage.Body.Span)
                                : null;

                        message = new Message(transportMessage.Headers, dataUri);
                    }
                    else
                    {
                        dataUri =
                            transportMessage.Body.Length != 0
                                ? "data:UnknownType;base64," + Convert.ToBase64String(transportMessage.Body.Span)
                                : null;

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
                    "An exception occurred when process received message. Message:'{Message}'.",
                    transportMessage
                );

                await client.RejectAsync(sender);

                _TracingError(tracingTimestamp, transportMessage, client.BrokerAddress, e);
            }
        };
    }

    private void _WriteLog(LogMessageEventArgs logMessage)
    {
        switch (logMessage.LogType)
        {
            case MqLogType.ConsumerCancelled:
                _isHealthy = false;
                _logger.LogWarning("RabbitMQ consumer cancelled. --> {Reason}", logMessage.Reason);
                break;
            case MqLogType.ConsumerRegistered:
                _isHealthy = true;
                _logger.LogInformation("RabbitMQ consumer registered. --> {Reason}", logMessage.Reason);
                break;
            case MqLogType.ConsumerUnregistered:
                _logger.LogWarning("RabbitMQ consumer unregistered. --> {Reason}", logMessage.Reason);
                break;
            case MqLogType.ConsumerShutdown:
                _isHealthy = false;
                _logger.LogWarning("RabbitMQ consumer shutdown. --> {Reason}", logMessage.Reason);
                break;
            case MqLogType.ConsumeError:
                _logger.LogError("Kafka client consume error. --> {Reason}", logMessage.Reason);
                break;
            case MqLogType.ConsumeRetries:
                _logger.LogWarning("Kafka client consume exception, retying... --> {Reason}", logMessage.Reason);
                break;
            case MqLogType.ServerConnError:
                _isHealthy = false;
                _logger.LogCritical("Kafka server connection error. --> {Reason}", logMessage.Reason);
                break;
            case MqLogType.ExceptionReceived:
                _logger.LogError("AzureServiceBus subscriber received an error. --> {Reason}", logMessage.Reason);
                break;
            case MqLogType.AsyncErrorEvent:
                _logger.LogError("NATS subscriber received an error. --> {Reason}", logMessage.Reason);
                break;
            case MqLogType.ConnectError:
                _isHealthy = false;
                _logger.LogError("NATS server connection error. --> {Reason}", logMessage.Reason);
                break;
            case MqLogType.InvalidIdFormat:
                _logger.LogError(
                    "AmazonSQS subscriber delete inflight message failed, invalid id. --> {Reason}",
                    logMessage.Reason
                );
                break;
            case MqLogType.MessageNotInflight:
                _logger.LogError(
                    "AmazonSQS subscriber change message's visibility failed, message isn't in flight. --> {Reason}",
                    logMessage.Reason
                );
                break;
            case MqLogType.RedisConsumeError:
                _isHealthy = true;
                _logger.LogError("Redis client consume error. --> {Reason}", logMessage.Reason);
                break;
            default:
                throw new InvalidOperationException($"Unknown {nameof(MqLogType)}={logMessage.LogType}");
        }
    }

    #region Tracing

    private static long? _TracingBefore(TransportMessage message, BrokerAddress broker)
    {
        if (_DiagnosticListener.IsEnabled(MessageDiagnosticListenerNames.BeforeConsume))
        {
            var eventData = new MessageEventDataSubStore
            {
                OperationTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Operation = message.GetName(),
                BrokerAddress = broker,
                TransportMessage = message,
            };

            _DiagnosticListener.Write(MessageDiagnosticListenerNames.BeforeConsume, eventData);

            return eventData.OperationTimestamp;
        }

        return null;
    }

    private static void _TracingAfter(long? tracingTimestamp, TransportMessage message, BrokerAddress broker)
    {
        MessageEventCounterSource.Log.WriteConsumeMetrics();
        if (tracingTimestamp != null && _DiagnosticListener.IsEnabled(MessageDiagnosticListenerNames.AfterConsume))
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

            _DiagnosticListener.Write(MessageDiagnosticListenerNames.AfterConsume, eventData);
        }
    }

    private static void _TracingError(
        long? tracingTimestamp,
        TransportMessage message,
        BrokerAddress broker,
        Exception ex
    )
    {
        if (tracingTimestamp != null && _DiagnosticListener.IsEnabled(MessageDiagnosticListenerNames.ErrorConsume))
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

            _DiagnosticListener.Write(MessageDiagnosticListenerNames.ErrorConsume, eventData);
        }
    }

    #endregion
}
