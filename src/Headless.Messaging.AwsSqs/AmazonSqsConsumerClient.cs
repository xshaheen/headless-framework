// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Amazon.Auth.AccessControlPolicy;
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using Amazon.SQS;
using Amazon.SQS.Model;
using Framework.Checks;
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
    private static readonly Lock _ConnectionLock = new();
    private readonly AmazonSqsOptions _amazonSqsOptions = options.Value;
    private readonly ILogger _logger = logger;
    private readonly SemaphoreSlim _semaphore = new(groupConcurrent);
    private string _queueUrl = string.Empty;

    private IAmazonSimpleNotificationService? _snsClient;
    private IAmazonSQS? _sqsClient;

    public Func<TransportMessage, object?, Task>? OnMessageCallback { get; set; }

    public Action<LogMessageEventArgs>? OnLogCallback { get; set; }

    public BrokerAddress BrokerAddress => new("aws_sqs", _queueUrl);

    public async ValueTask<ICollection<string>> FetchTopicsAsync(IEnumerable<string> topicNames)
    {
        Argument.IsNotNull(topicNames);

        await _ConnectAsync(true, false).AnyContext();

        var topicArns = new List<string>();
        foreach (var topic in topicNames)
        {
            var createTopicRequest = new CreateTopicRequest(topic.NormalizeForAws());

            var createTopicResponse = await _snsClient!.CreateTopicAsync(createTopicRequest).AnyContext();

            topicArns.Add(createTopicResponse.TopicArn);
        }

        await _GenerateSqsAccessPolicyAsync(topicArns).AnyContext();

        return topicArns;
    }

    public async ValueTask SubscribeAsync(IEnumerable<string> topics)
    {
        Argument.IsNotNull(topics);

        await _ConnectAsync().AnyContext();

        await _SubscribeToTopics(topics).AnyContext();
    }

    public async ValueTask ListeningAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        await _ConnectAsync().AnyContext();

        var request = new ReceiveMessageRequest(_queueUrl) { WaitTimeSeconds = 5, MaxNumberOfMessages = 1 };

        while (true)
        {
            var response = await _sqsClient!.ReceiveMessageAsync(request, cancellationToken).AnyContext();

            if (response.Messages.Count == 1)
            {
                if (groupConcurrent > 0)
                {
                    await _semaphore.WaitAsync(cancellationToken).AnyContext();
                    _ = Task.Run(
                            async () =>
                            {
                                try
                                {
                                    await consumeAsync().AnyContext();
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogError(ex, "Error consuming message for group {GroupId}", groupId);
                                    _semaphore.Release();

                                    try
                                    {
                                        await RejectAsync(response.Messages[0].ReceiptHandle).AnyContext();
                                    }
                                    catch (Exception rejectEx)
                                    {
                                        _logger.LogError(
                                            rejectEx,
                                            "Failed to reject message after consume error for group {GroupId}",
                                            groupId
                                        );
                                    }
                                }
                            },
                            cancellationToken
                        )
                        .AnyContext();
                }
                else
                {
                    await consumeAsync().AnyContext();
                }

                async Task consumeAsync()
                {
                    var receiptHandle = response.Messages[0].ReceiptHandle;

                    try
                    {
                        var messageObj = JsonSerializer.Deserialize<SqsReceivedMessage>(response.Messages[0].Body);

                        if (messageObj?.MessageAttributes == null)
                        {
                            _logger.LogError(
                                "Invalid SQS message structure: deserialization returned null or missing MessageAttributes. Moving to DLQ."
                            );
                            await RejectAsync(receiptHandle).AnyContext();
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

                        await OnMessageCallback!(message, receiptHandle).AnyContext();
                    }
                    catch (JsonException ex)
                    {
                        _logger.LogError(ex, "Failed to deserialize SQS message. Moving to DLQ.");
                        await RejectAsync(receiptHandle).AnyContext();
                    }
                }
            }
            else
            {
                cancellationToken.ThrowIfCancellationRequested();
                cancellationToken.WaitHandle.WaitOne(timeout);
            }
        }
    }

    public async ValueTask CommitAsync(object? sender)
    {
        try
        {
            await _sqsClient!.DeleteMessageAsync(_queueUrl, (string)sender!).AnyContext();
        }
        catch (ReceiptHandleIsInvalidException ex)
        {
            _InvalidIdFormatLog(ex.Message);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async ValueTask RejectAsync(object? sender)
    {
        try
        {
            await _sqsClient!.ChangeMessageVisibilityAsync(_queueUrl, (string)sender!, 3).AnyContext();
        }
        catch (MessageNotInflightException ex)
        {
            _MessageNotInflightLog(ex.Message);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public ValueTask DisposeAsync()
    {
        _sqsClient?.Dispose();
        _snsClient?.Dispose();
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
                lock (_ConnectionLock)
                {
#pragma warning disable CA1508 // False positive: Dead code analysis - the field is initialized inside the lock
                    if (_sqsClient == null)
#pragma warning restore CA1508
                    {
                        _snsClient = AwsClientFactory.CreateSnsClient(_amazonSqsOptions);
                    }
                }
            }

            if (_sqsClient == null && initSqs)
            {
                lock (_ConnectionLock)
                {
#pragma warning disable CA1508 // False positive: Dead code analysis - the field is initialized inside the lock
                    _sqsClient ??= AwsClientFactory.CreateSqsClient(_amazonSqsOptions);
#pragma warning restore CA1508
                }

                if (string.IsNullOrWhiteSpace(_queueUrl))
                {
                    // Create or get existing queue URL asynchronously
                    var queueResponse = await _sqsClient!.CreateQueueAsync(groupId.NormalizeForAws()).AnyContext();
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
        await _ConnectAsync(false, true).AnyContext();

        var queueAttributes = await _sqsClient!.GetAttributesAsync(_queueUrl).AnyContext();

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
        await _sqsClient.SetAttributesAsync(_queueUrl, setAttributes).AnyContext();
    }

    private async Task _SubscribeToTopics(IEnumerable<string> topics)
    {
        var queueAttributes = await _sqsClient!.GetAttributesAsync(_queueUrl).AnyContext();

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
                .AnyContext();
        }
    }

    #endregion
}
