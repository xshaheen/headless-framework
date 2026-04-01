// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Amazon.Auth.AccessControlPolicy;
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using Amazon.SQS;
using Amazon.SQS.Model;
using Headless.Checks;
using Headless.Messaging.Messages;
using Headless.Messaging.Transport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.AwsSqs;

internal sealed class AmazonSqsConsumerClient(
    string groupId,
    byte groupConcurrent,
    IOptions<AmazonSqsOptions> options,
    ILogger<AmazonSqsConsumerClient> logger
) : IConsumerClient
{
    private readonly Lock _connectionLock = new();
    private readonly AmazonSqsOptions _amazonSqsOptions = options.Value;
    private readonly ILogger _logger = logger;
    private readonly SemaphoreSlim _semaphore = new(groupConcurrent);
    private readonly ConsumerPauseGate _pauseGate = new();
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _disposed;
    private string _queueUrl = string.Empty;

    private IAmazonSimpleNotificationService? _snsClient;
    private IAmazonSQS? _sqsClient;

    public Func<TransportMessage, object?, Task>? OnMessageCallback { get; set; }

    public Action<LogMessageEventArgs>? OnLogCallback { get; set; }

    public BrokerAddress BrokerAddress => new("aws_sqs", _queueUrl);

    public async ValueTask<ICollection<string>> FetchTopicsAsync(IEnumerable<string> topicNames)
    {
        Argument.IsNotNull(topicNames);

        await _ConnectAsync(true, false).ConfigureAwait(false);

        var topicArns = new List<string>();
        foreach (var topic in topicNames)
        {
            var createTopicRequest = new CreateTopicRequest(topic.NormalizeForAws());

            var createTopicResponse = await _snsClient!.CreateTopicAsync(createTopicRequest).ConfigureAwait(false);

            topicArns.Add(createTopicResponse.TopicArn);
        }

        await _GenerateSqsAccessPolicyAsync(topicArns).ConfigureAwait(false);

        return topicArns;
    }

    public async ValueTask SubscribeAsync(IEnumerable<string> topics)
    {
        Argument.IsNotNull(topics);

        await _ConnectAsync().ConfigureAwait(false);

        await _SubscribeToTopics(topics).ConfigureAwait(false);
        _ready.TrySetResult();
    }

    public ValueTask WaitUntilReadyAsync(CancellationToken cancellationToken = default)
    {
        return new ValueTask(_ready.Task.WaitAsync(cancellationToken));
    }

    public async ValueTask ListeningAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        await _ConnectAsync().ConfigureAwait(false);

        var request = new ReceiveMessageRequest(_queueUrl) { WaitTimeSeconds = 5, MaxNumberOfMessages = 10 };

        while (true)
        {
            await _pauseGate.WaitIfPausedAsync(cancellationToken).ConfigureAwait(false);

            var response = await _sqsClient!.ReceiveMessageAsync(request, cancellationToken).ConfigureAwait(false);

            if (response?.Messages?.Count > 0)
            {
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
                                        await consumeAsync(sqsMessage).ConfigureAwait(false);
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
                        await consumeAsync(sqsMessage).ConfigureAwait(false);
                    }
                }
            }
            else
            {
                cancellationToken.ThrowIfCancellationRequested();
                cancellationToken.WaitHandle.WaitOne(timeout);
            }
        }

        async Task consumeAsync(Amazon.SQS.Model.Message sqsMessage)
        {
            var receiptHandle = sqsMessage.ReceiptHandle;

            SqsReceivedMessage? messageObj;
            try
            {
                messageObj = JsonSerializer.Deserialize<SqsReceivedMessage>(sqsMessage.Body);
            }
            catch (JsonException ex)
            {
                _logger.SqsMessageDeserializationFailed(ex);
                await rejectSafelyAsync(receiptHandle).ConfigureAwait(false);
                return;
            }

            if (messageObj?.MessageAttributes == null)
            {
                _logger.InvalidSqsMessageStructure();
                await rejectSafelyAsync(receiptHandle).ConfigureAwait(false);
                return;
            }

            var header = messageObj.MessageAttributes.ToDictionary<
                KeyValuePair<string, SqsReceivedMessageAttributes>,
                string,
                string?
            >(x => x.Key, x => x.Value?.Value ?? string.Empty, StringComparer.Ordinal);
            var body = messageObj.Message;

            var message = new TransportMessage(header, body != null ? Encoding.UTF8.GetBytes(body) : null)
            {
                Headers = { [Headers.Group] = groupId },
            };

            await OnMessageCallback!(message, receiptHandle).ConfigureAwait(false);
        }

        async Task rejectSafelyAsync(string receiptHandle)
        {
            try
            {
                await RejectAsync(receiptHandle).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.SqsRejectFailed(ex, groupId);
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
        try
        {
            await _sqsClient!.DeleteMessageAsync(_queueUrl, (string)sender!).ConfigureAwait(false);
        }
        catch (ReceiptHandleIsInvalidException ex)
        {
            _InvalidIdFormatLog(ex.Message);
        }
    }

    public async ValueTask RejectAsync(object? sender)
    {
        try
        {
            await _sqsClient!.ChangeMessageVisibilityAsync(_queueUrl, (string)sender!, 3).ConfigureAwait(false);
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

    public async ValueTask PauseAsync(CancellationToken cancellationToken = default) => await _pauseGate.PauseAsync();

    public async ValueTask ResumeAsync(CancellationToken cancellationToken = default) => await _pauseGate.ResumeAsync();

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
    private async Task _ConnectAsync(bool initSns = true, bool initSqs = true)
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

                if (string.IsNullOrWhiteSpace(_queueUrl))
                {
                    // Create or get existing queue URL asynchronously
                    var queueResponse = await _sqsClient!
                        .CreateQueueAsync(groupId.NormalizeForAws())
                        .ConfigureAwait(false);
                    _queueUrl = queueResponse.QueueUrl;
                }
            }
        }
    }

    #region private methods

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

    private async Task _GenerateSqsAccessPolicyAsync(IEnumerable<string> topicArns)
    {
        await _ConnectAsync(false, true).ConfigureAwait(false);

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

    private async Task _SubscribeToTopics(IEnumerable<string> topics)
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
                    }
                )
                .ConfigureAwait(false);
        }
    }

    #endregion
}

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

    [LoggerMessage(EventId = 4202, Level = LogLevel.Error, Message = "Failed to reject message for group {GroupId}")]
    public static partial void SqsRejectFailed(this ILogger logger, Exception exception, string groupId);

    [LoggerMessage(EventId = 4203, Level = LogLevel.Error, Message = "Error consuming message for group {GroupId}")]
    public static partial void SqsMessageConsumeFailed(this ILogger logger, Exception exception, string groupId);
}
