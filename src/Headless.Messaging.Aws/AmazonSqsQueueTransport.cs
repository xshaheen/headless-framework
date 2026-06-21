// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.Aws;

internal sealed class AmazonSqsQueueTransport(
    ILogger<AmazonSqsQueueTransport> logger,
    IOptions<AmazonSqsOptions> sqsOptionsAccessor
) : IQueueTransport
{
    private const int _MaxMessageAttributes = 10;
    private readonly ConcurrentDictionary<string, string> _queueUrlMaps = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private IAmazonSQS? _sqsClient;

    public BrokerAddress BrokerAddress => new("aws_sqs", _GetBrokerEndpoint());

    public async Task<OperateResult> SendAsync(TransportMessage message, CancellationToken cancellationToken = default)
    {
        try
        {
            var queueName = message.GetName().NormalizeForSqsQueueName();
            var queueUrl = await _GetOrCreateQueueUrlAsync(queueName, cancellationToken).ConfigureAwait(false);
            var body = message.Body.Length > 0 ? Encoding.UTF8.GetString(message.Body.Span) : string.Empty;
            var attributes = message
                .Headers.Where(x => x.Value != null)
                .ToDictionary(
                    x => x.Key,
                    x => new MessageAttributeValue { StringValue = x.Value, DataType = "String" },
                    StringComparer.Ordinal
                );

            if (attributes.Count > _MaxMessageAttributes)
            {
                return OperateResult.Failed(
                    new InvalidOperationException("AWS SQS supports at most 10 message attributes."),
                    new OperateError
                    {
                        Code = "AWS_SQS_MESSAGE_ATTRIBUTES_LIMIT",
                        Description = "AWS SQS supports at most 10 message attributes.",
                    }
                );
            }

            var request = new SendMessageRequest
            {
                QueueUrl = queueUrl,
                MessageBody = body,
                MessageAttributes = attributes,
            };

            if (queueName.IsAwsFifoName())
            {
                request.MessageGroupId = _ResolveMessageGroupId(message);

                if (
                    message.Headers.TryGetValue(Headers.MessageId, out var messageId)
                    && !string.IsNullOrWhiteSpace(messageId)
                )
                {
                    request.MessageDeduplicationId = messageId;
                }
            }

            await _sqsClient!.SendMessageAsync(request, cancellationToken).ConfigureAwait(false);

            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogSqsMessageEnqueued(queueName);
            }

            return OperateResult.Success;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var wrapperEx = new PublisherSentFailedException(ex.Message, ex);
            var errors = new OperateError
            {
                Code = ex.HResult.ToString(CultureInfo.InvariantCulture),
                Description = ex.Message,
            };

            return OperateResult.Failed(wrapperEx, errors);
        }
    }

    private static string _ResolveMessageGroupId(TransportMessage message)
    {
        if (
            message.Headers.TryGetValue(AwsMessagingHeaders.MessageGroupId, out var messageGroupId)
            && !string.IsNullOrWhiteSpace(messageGroupId)
        )
        {
            return messageGroupId;
        }

        return message.GetGroup() ?? "default";
    }

    public ValueTask DisposeAsync()
    {
        _semaphore.Dispose();
        _sqsClient?.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task<string> _GetOrCreateQueueUrlAsync(string queueName, CancellationToken cancellationToken)
    {
        if (_queueUrlMaps.TryGetValue(queueName, out var queueUrl))
        {
            return queueUrl;
        }

        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (_queueUrlMaps.TryGetValue(queueName, out queueUrl))
            {
                return queueUrl;
            }

            _sqsClient ??= AwsClientFactory.CreateSqsClient(sqsOptionsAccessor.Value);
            var response = queueName.IsAwsFifoName()
                ? await _sqsClient
                    .CreateQueueAsync(queueName.ToSqsCreateQueueRequest(), cancellationToken)
                    .ConfigureAwait(false)
                : await _sqsClient.CreateQueueAsync(queueName, cancellationToken).ConfigureAwait(false);
            _queueUrlMaps[queueName] = response.QueueUrl;

            return response.QueueUrl;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private string _GetBrokerEndpoint()
    {
        var options = sqsOptionsAccessor.Value;
        return AwsBrokerEndpoint.Resolve(options.SqsServiceUrl, "sqs", options);
    }
}

internal static partial class AmazonSqsQueueTransportLog
{
    [LoggerMessage(
        EventId = 4,
        EventName = "SqsMessageEnqueued",
        Level = LogLevel.Debug,
        Message = "SQS message [{QueueName}] has been enqueued."
    )]
    public static partial void LogSqsMessageEnqueued(this ILogger logger, string queueName);
}
