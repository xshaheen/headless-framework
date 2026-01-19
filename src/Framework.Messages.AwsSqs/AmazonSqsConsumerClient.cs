// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Amazon.Auth.AccessControlPolicy;
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using Amazon.SQS;
using Amazon.SQS.Model;
using Framework.Checks;
using Framework.Messages.Internal;
using Framework.Messages.Messages;
using Framework.Messages.Transport;
using Microsoft.Extensions.Options;

namespace Framework.Messages;

internal sealed class AmazonSqsConsumerClient(string groupId, byte groupConcurrent, IOptions<AmazonSqsOptions> options)
    : IConsumerClient
{
    private static readonly Lock _ConnectionLock = new();
    private readonly AmazonSqsOptions _amazonSqsOptions = options.Value;
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
    }

    public async ValueTask ListeningAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        await _ConnectAsync().ConfigureAwait(false);

        var request = new ReceiveMessageRequest(_queueUrl) { WaitTimeSeconds = 5, MaxNumberOfMessages = 1 };

        while (true)
        {
            var response = await _sqsClient!.ReceiveMessageAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.Messages.Count == 1)
            {
                if (groupConcurrent > 0)
                {
                    await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                    _ = Task.Run(consumeAsync, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await consumeAsync().ConfigureAwait(false);
                }

                Task consumeAsync()
                {
                    var messageObj = JsonSerializer.Deserialize<SqsReceivedMessage>(response.Messages[0].Body);

                    var header = messageObj!.MessageAttributes.ToDictionary(
                        x => x.Key,
                        x => x.Value.Value,
                        StringComparer.Ordinal
                    );
                    var body = messageObj.Message;

                    var message = new TransportMessage(header, body != null ? Encoding.UTF8.GetBytes(body) : null)
                    {
                        Headers = { [Headers.Group] = groupId },
                    };

                    return OnMessageCallback!(message, response.Messages[0].ReceiptHandle);
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
            await _sqsClient!.DeleteMessageAsync(_queueUrl, (string)sender!).ConfigureAwait(false);
            _semaphore.Release();
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
            _semaphore.Release();
        }
        catch (MessageNotInflightException ex)
        {
            _MessageNotInflightLog(ex.Message);
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
                        if (string.IsNullOrWhiteSpace(_amazonSqsOptions.SnsServiceUrl))
                        {
                            _snsClient =
                                _amazonSqsOptions.Credentials != null
                                    ? new AmazonSimpleNotificationServiceClient(
                                        _amazonSqsOptions.Credentials,
                                        _amazonSqsOptions.Region
                                    )
                                    : new AmazonSimpleNotificationServiceClient(_amazonSqsOptions.Region);
                        }
                        else
                        {
                            _snsClient =
                                _amazonSqsOptions.Credentials != null
                                    ? new AmazonSimpleNotificationServiceClient(
                                        _amazonSqsOptions.Credentials,
                                        new AmazonSimpleNotificationServiceConfig
                                        {
                                            ServiceURL = _amazonSqsOptions.SnsServiceUrl,
                                        }
                                    )
                                    : new AmazonSimpleNotificationServiceClient(
                                        new AmazonSimpleNotificationServiceConfig
                                        {
                                            ServiceURL = _amazonSqsOptions.SnsServiceUrl,
                                        }
                                    );
                        }
                    }
                }
            }

            if (_sqsClient == null && initSqs)
            {
                lock (_ConnectionLock)
                {
#pragma warning disable CA1508 // False positive: Dead code analysis - the field is initialized inside the lock
                    if (_sqsClient == null)
#pragma warning restore CA1508
                    {
                        if (string.IsNullOrWhiteSpace(_amazonSqsOptions.SqsServiceUrl))
                        {
                            _sqsClient =
                                _amazonSqsOptions.Credentials != null
                                    ? new AmazonSQSClient(_amazonSqsOptions.Credentials, _amazonSqsOptions.Region)
                                    : new AmazonSQSClient(_amazonSqsOptions.Region);
                        }
                        else
                        {
                            _sqsClient =
                                _amazonSqsOptions.Credentials != null
                                    ? new AmazonSQSClient(
                                        _amazonSqsOptions.Credentials,
                                        new AmazonSQSConfig { ServiceURL = _amazonSqsOptions.SqsServiceUrl }
                                    )
                                    : new AmazonSQSClient(
                                        new AmazonSQSConfig { ServiceURL = _amazonSqsOptions.SqsServiceUrl }
                                    );
                        }
                    }
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
        policy.CompactSqsPermissions(sqsQueueArn);

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
