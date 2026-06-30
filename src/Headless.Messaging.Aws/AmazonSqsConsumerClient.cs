// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Amazon.Auth.AccessControlPolicy;
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using Amazon.SQS;
using Amazon.SQS.Model;
using Headless.Checks;
using Headless.Messaging.Transport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.Aws;

internal sealed class AmazonSqsConsumerClient(
    string groupId,
    byte groupConcurrent,
    IOptions<AmazonSqsOptions> options,
    ILogger<AmazonSqsConsumerClient> logger,
    IntentType intentType = IntentType.Bus,
    TimeProvider? timeProvider = null
) : IConsumerClient
{
    private readonly Lock _connectionLock = new();
    private readonly Lock _queueUrlsLock = new();
    private readonly AmazonSqsOptions _amazonSqsOptions = options.Value;
    private readonly ILogger _logger = logger;
    private readonly SemaphoreSlim _semaphore = new(groupConcurrent);
    private readonly ConsumerPauseGate _pauseGate = new();
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private ImmutableArray<string> _queueUrls = ImmutableArray<string>.Empty;
    private int _disposed;
    private string _queueUrl = string.Empty;

    private IAmazonSimpleNotificationService? _snsClient;
    private IAmazonSQS? _sqsClient;

    public Func<TransportMessage, object?, Task>? OnMessageCallback { get; set; }

    public Action<LogMessageEventArgs>? OnLogCallback { get; set; }

    public BrokerAddress BrokerAddress => new("aws_sqs", _queueUrl);

    public async ValueTask<ICollection<string>> FetchMessageNamesAsync(IEnumerable<string> messageNames)
    {
        Argument.IsNotNull(messageNames);

        var cancellationToken = CancellationToken.None;

        if (intentType == IntentType.Queue)
        {
            await _ConnectAsync(false, true, cancellationToken).ConfigureAwait(false);

            var queueUrls = new List<string>();
            foreach (var topic in messageNames)
            {
                var queueResponse = await _sqsClient!
                    .CreateQueueAsync(topic.ToSqsCreateQueueRequest(), cancellationToken)
                    .ConfigureAwait(false);

                queueUrls.Add(queueResponse.QueueUrl);
            }

            _SetQueueUrls([.. queueUrls]);

            return queueUrls;
        }

        await _ConnectAsync(true, false, cancellationToken).ConfigureAwait(false);

        var topicArns = new List<string>();
        foreach (var topic in messageNames)
        {
            var createTopicRequest = topic.ToSnsCreateTopicRequest();

            var createTopicResponse = await _snsClient!
                .CreateTopicAsync(createTopicRequest, cancellationToken)
                .ConfigureAwait(false);

            topicArns.Add(createTopicResponse.TopicArn);
        }

        await _GenerateSqsAccessPolicyAsync(topicArns, cancellationToken).ConfigureAwait(false);

        return topicArns;
    }

    public async ValueTask SubscribeAsync(IEnumerable<string> topics)
    {
        Argument.IsNotNull(topics);

        var cancellationToken = CancellationToken.None;

        if (intentType == IntentType.Queue)
        {
            await _ConnectAsync(false, true, cancellationToken).ConfigureAwait(false);
            _SetQueueUrls([.. topics]);
            _ready.TrySetResult();
            return;
        }

        await _ConnectAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        await _SubscribeToTopics(topics, cancellationToken).ConfigureAwait(false);
        _ready.TrySetResult();
    }

    public ValueTask WaitUntilReadyAsync(CancellationToken cancellationToken = default)
    {
        return new ValueTask(_ready.Task.WaitAsync(cancellationToken));
    }

    public async ValueTask ListeningAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        await _ConnectAsync(intentType == IntentType.Bus, true, cancellationToken).ConfigureAwait(false);

        if (intentType == IntentType.Bus && _GetQueueUrlsSnapshot().Length == 0)
        {
            _SetQueueUrls([_queueUrl]);
        }

        var retryDelay = TimeSpan.FromSeconds(1);

        while (true)
        {
            await _pauseGate.WaitIfPausedAsync(cancellationToken).ConfigureAwait(false);

            (string QueueUrl, ReceiveMessageResponse Response)[] responses;
            try
            {
                var snapshot = _GetQueueUrlsSnapshot();
                responses = await Task.WhenAll(
                        snapshot.Select(queueUrl => _ReceiveMessagesAsync(queueUrl, cancellationToken))
                    )
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.SqsReceiveFailed(ex, groupId);
                retryDelay = _NextBackoff(retryDelay);
                await _timeProvider.Delay(retryDelay, cancellationToken).ConfigureAwait(false);
                continue;
            }

            retryDelay = TimeSpan.FromSeconds(1);

            var receivedAny = false;
            foreach (var (queueUrl, response) in responses)
            {
                if (response?.Messages?.Count > 0)
                {
                    receivedAny = true;

                    foreach (var sqsMessage in response.Messages)
                    {
                        if (groupConcurrent > 0)
                        {
                            await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                            _ObserveBackgroundHandler(
                                Task.Run(
                                    async () =>
                                    {
                                        try
                                        {
                                            await consumeAsync(queueUrl, sqsMessage).ConfigureAwait(false);
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
                            await consumeAsync(queueUrl, sqsMessage).ConfigureAwait(false);
                        }
                    }
                }
            }

            if (!receivedAny)
            {
                await _timeProvider.Delay(timeout, cancellationToken).ConfigureAwait(false);
            }
        }

        async Task consumeAsync(string queueUrl, Message sqsMessage)
        {
            var receiptHandle = sqsMessage.ReceiptHandle;
            var (header, body) = await _ReadMessageAsync(sqsMessage, receiptHandle).ConfigureAwait(false);

            if (header is null)
            {
                return;
            }

            var message = new TransportMessage(header, body != null ? Encoding.UTF8.GetBytes(body) : null)
            {
                Headers = { [Headers.Group] = groupId },
            };

            await OnMessageCallback!(message, new InflightSqsMessage(queueUrl, receiptHandle)).ConfigureAwait(false);
        }

        // ReSharper disable once FunctionNeverReturns
    }

    private async Task<(string QueueUrl, ReceiveMessageResponse Response)> _ReceiveMessagesAsync(
        string queueUrl,
        CancellationToken cancellationToken
    )
    {
        var request = new ReceiveMessageRequest(queueUrl)
        {
            WaitTimeSeconds = 5,
            MaxNumberOfMessages = 10,
            MessageAttributeNames = ["All"],
        };
        var response = await _sqsClient!.ReceiveMessageAsync(request, cancellationToken).ConfigureAwait(false);
        return (queueUrl, response);
    }

    private ImmutableArray<string> _GetQueueUrlsSnapshot()
    {
        lock (_queueUrlsLock)
        {
            return _queueUrls;
        }
    }

    private void _SetQueueUrls(ImmutableArray<string> queueUrls)
    {
        lock (_queueUrlsLock)
        {
            _queueUrls = queueUrls;
        }
    }

    private static TimeSpan _NextBackoff(TimeSpan current)
    {
        // Floor at 200ms, ceiling at 30s — jittered exponential backoff for transient SQS errors.
        var floor = TimeSpan.FromMilliseconds(200);
        var ceiling = TimeSpan.FromSeconds(30);
        var doubled = TimeSpan.FromTicks(Math.Max(current.Ticks * 2, floor.Ticks));
        var capped = doubled > ceiling ? ceiling : doubled;
        var jitterMs = Random.Shared.Next(0, (int)Math.Max(1, capped.TotalMilliseconds / 4));
        return capped + TimeSpan.FromMilliseconds(jitterMs);
    }

    private void _ObserveBackgroundHandler(Task task)
    {
        _ = task.ContinueWith(
            completedTask =>
            {
                var exception = completedTask.Exception?.GetBaseException();
                if (exception is not null)
                {
                    _logger.SqsMessageConsumeFailed(exception, groupId);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default
        );
    }

    public async ValueTask CommitAsync(object? sender)
    {
        var inflight = _GetInflightMessage(sender);

        try
        {
            await _sqsClient!.DeleteMessageAsync(inflight.QueueUrl, inflight.ReceiptHandle).ConfigureAwait(false);
        }
        catch (ReceiptHandleIsInvalidException ex)
        {
            _InvalidIdFormatLog(ex.Message);
        }
    }

    public async ValueTask RejectAsync(object? sender)
    {
        var inflight = _GetInflightMessage(sender);

        try
        {
            await _sqsClient!
                .ChangeMessageVisibilityAsync(inflight.QueueUrl, inflight.ReceiptHandle, 3)
                .ConfigureAwait(false);
        }
        catch (MessageNotInflightException ex)
        {
            _MessageNotInflightLog(ex.Message);
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

    public async ValueTask PauseAsync(CancellationToken cancellationToken = default) =>
        await _pauseGate.PauseAsync().ConfigureAwait(false);

    public async ValueTask ResumeAsync(CancellationToken cancellationToken = default) =>
        await _pauseGate.ResumeAsync().ConfigureAwait(false);

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return ValueTask.CompletedTask;
        }

        _pauseGate.Release();
        _ready.TrySetCanceled();
        _sqsClient?.Dispose();
        _snsClient?.Dispose();
        _semaphore.Dispose();
        return ValueTask.CompletedTask;
    }

    // Asynchronous version of Connect to avoid blocking threads during queue creation
    private async Task _ConnectAsync(
        bool initSns = true,
        bool initSqs = true,
        CancellationToken cancellationToken = default
    )
    {
        // Fast path if already initialized for requested resources
        if ((initSns && _snsClient == null) || (initSqs && _sqsClient == null))
        {
            if (_snsClient == null && initSns)
            {
                lock (_connectionLock)
                {
#pragma warning disable CA1508 // Justification: other thread can initialize it
                    _snsClient ??= AwsClientFactory.CreateSnsClient(_amazonSqsOptions);
#pragma warning restore CA1508
                }
            }

            if (_sqsClient == null && initSqs)
            {
                lock (_connectionLock)
                {
#pragma warning disable CA1508 // Justification: other thread can initialize it
                    _sqsClient ??= AwsClientFactory.CreateSqsClient(_amazonSqsOptions);
#pragma warning restore CA1508
                }

                if (intentType == IntentType.Bus && string.IsNullOrWhiteSpace(_queueUrl))
                {
                    // Create or get existing queue URL asynchronously
                    var queueResponse = await _sqsClient
                        .CreateQueueAsync(groupId.ToSqsCreateQueueRequest(), cancellationToken)
                        .ConfigureAwait(false);
                    _queueUrl = queueResponse.QueueUrl;
                }
            }
        }
    }

    #region private methods

    private async Task<(Dictionary<string, string?>? Headers, string? Body)> _ReadMessageAsync(
        Message sqsMessage,
        string receiptHandle
    )
    {
        if (intentType == IntentType.Queue)
        {
            var headers = sqsMessage.MessageAttributes.ToDictionary<
                KeyValuePair<string, Amazon.SQS.Model.MessageAttributeValue>,
                string,
                string?
            >(x => x.Key, x => x.Value.StringValue, StringComparer.Ordinal);

            return (headers, sqsMessage.Body);
        }

        SqsReceivedMessage? messageObj;
        try
        {
            messageObj = JsonSerializer.Deserialize<SqsReceivedMessage>(sqsMessage.Body);
        }
        catch (JsonException ex)
        {
            _logger.SqsMessageDeserializationFailed(ex);
            await RejectAsync(new InflightSqsMessage(_queueUrl, receiptHandle)).ConfigureAwait(false);
            return (null, null);
        }

        if (messageObj?.MessageAttributes == null)
        {
            _logger.InvalidSqsMessageStructure();
            await RejectAsync(new InflightSqsMessage(_queueUrl, receiptHandle)).ConfigureAwait(false);
            return (null, null);
        }

        var header = messageObj.MessageAttributes.ToDictionary<
            KeyValuePair<string, SqsReceivedMessageAttributes>,
            string,
            string?
        >(x => x.Key, x => x.Value?.Value ?? string.Empty, StringComparer.Ordinal);

        return (header, messageObj.Message);
    }

    private InflightSqsMessage _GetInflightMessage(object? sender)
    {
        return sender switch
        {
            InflightSqsMessage inflight => inflight,
            string receiptHandle => new InflightSqsMessage(_queueUrl, receiptHandle),
            _ => throw new InvalidOperationException("SQS commit state is missing the receipt handle."),
        };
    }

    private void _InvalidIdFormatLog(string exceptionMessage)
    {
        var logArgs = new LogMessageEventArgs { LogType = MqLogType.InvalidIdFormat, Reason = exceptionMessage };

        OnLogCallback!(logArgs);
    }

    private void _MessageNotInflightLog(string exceptionMessage)
    {
        var logArgs = new LogMessageEventArgs { LogType = MqLogType.MessageNotInflight, Reason = exceptionMessage };

        OnLogCallback!(logArgs);
    }

    private async Task _GenerateSqsAccessPolicyAsync(IEnumerable<string> topicArns, CancellationToken cancellationToken)
    {
        await _ConnectAsync(false, true, cancellationToken).ConfigureAwait(false);

        var queueAttributes = await _sqsClient!.GetAttributesAsync(_queueUrl).ConfigureAwait(false);

        var sqsQueueArn = queueAttributes["QueueArn"];

        var policy =
            queueAttributes.TryGetValue("Policy", out var policyStr) && !string.IsNullOrEmpty(policyStr)
                ? Policy.FromJson(policyStr)
                : new Policy();

        var topicArnsToAllow = topicArns.Where(a => !policy.HasSqsPermission(a, sqsQueueArn)).ToList();

        if (topicArnsToAllow.Count == 0)
        {
            return;
        }

        policy.AddSqsPermissions(topicArnsToAllow, sqsQueueArn);

        var setAttributes = new Dictionary<string, string>(StringComparer.Ordinal) { { "Policy", policy.ToJson() } };
        await _sqsClient.SetAttributesAsync(_queueUrl, setAttributes).ConfigureAwait(false);
    }

    private async Task _SubscribeToTopics(IEnumerable<string> topics, CancellationToken cancellationToken)
    {
        var queueAttributes = await _sqsClient!.GetAttributesAsync(_queueUrl).ConfigureAwait(false);

        var sqsQueueArn = queueAttributes["QueueArn"];
        foreach (var topicArn in topics)
        {
            await _snsClient!
                .SubscribeAsync(
                    new SubscribeRequest
                    {
                        TopicArn = topicArn,
                        Protocol = "sqs",
                        Endpoint = sqsQueueArn,
                    },
                    cancellationToken
                )
                .ConfigureAwait(false);
        }
    }

    #endregion
}

internal sealed record InflightSqsMessage(string QueueUrl, string ReceiptHandle);

internal static partial class AmazonSqsConsumerClientLog
{
    [LoggerMessage(
        EventId = 4200,
        Level = LogLevel.Error,
        Message = "Failed to deserialize SQS message. Moving to DLQ."
    )]
    public static partial void SqsMessageDeserializationFailed(this ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 4201,
        Level = LogLevel.Error,
        Message = "Invalid SQS message structure: deserialization returned null or missing MessageAttributes. Moving to DLQ."
    )]
    public static partial void InvalidSqsMessageStructure(this ILogger logger);

    [LoggerMessage(EventId = 4203, Level = LogLevel.Error, Message = "Error consuming message for group {GroupId}")]
    public static partial void SqsMessageConsumeFailed(this ILogger logger, Exception exception, string groupId);

    [LoggerMessage(
        EventId = 4204,
        Level = LogLevel.Warning,
        Message = "Failed to receive SQS messages for group {GroupId}; backing off before retry."
    )]
    public static partial void SqsReceiveFailed(this ILogger logger, Exception exception, string groupId);
}
