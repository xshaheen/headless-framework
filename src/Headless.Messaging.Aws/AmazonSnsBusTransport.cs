// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.Aws;

internal sealed class AmazonSnsBusTransport(
    ILogger<AmazonSnsBusTransport> logger,
    IOptions<AmazonSqsOptions> sqsOptionsAccessor
) : IBusTransport
{
    private readonly ILogger _logger = logger;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private IAmazonSimpleNotificationService? _snsClient;
    private ConcurrentDictionary<string, string>? _topicArnMaps;

    public BrokerAddress BrokerAddress => new("aws_sns", _GetBrokerEndpoint());

    public async Task<OperateResult> SendAsync(TransportMessage message, CancellationToken cancellationToken = default)
    {
        try
        {
            await _FetchExistingTopicArns(cancellationToken).ConfigureAwait(false);

            var normalizeForAws = message.GetName().NormalizeForAws();
            var (success, arn) = await _TryGetOrCreateTopicArnAsync(normalizeForAws, cancellationToken)
                .ConfigureAwait(false);

            if (success)
            {
                // SNS requires a non-null message body; use empty string for empty bodies
                var bodyJson = message.Body.Length > 0 ? Encoding.UTF8.GetString(message.Body.Span) : string.Empty;

                var attributes = message
                    .Headers.Where(x => x.Value != null)
                    .ToDictionary(
                        x => x.Key,
                        x => new MessageAttributeValue { StringValue = x.Value, DataType = "String" },
                        StringComparer.Ordinal
                    );

                var request = new PublishRequest(arn, bodyJson) { MessageAttributes = attributes };

                if (normalizeForAws.IsAwsFifoName())
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

                await _snsClient!.PublishAsync(request, cancellationToken).ConfigureAwait(false);

                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    _logger.LogSnsTopicMessagePublished(normalizeForAws);
                }

                return OperateResult.Success;
            }

            _logger.LogSnsTopicNotFound(normalizeForAws);

            return OperateResult.Failed(
                new PublisherSentFailedException($"Can't be found SNS topics for [{normalizeForAws}]"),
                new OperateError { Code = "SNS", Description = $"Can't be found SNS topics for [{normalizeForAws}]" }
            );
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

    private async Task _FetchExistingTopicArns(CancellationToken cancellationToken = default)
    {
        if (_topicArnMaps != null)
        {
            return;
        }

        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            _snsClient ??= AwsClientFactory.CreateSnsClient(sqsOptionsAccessor.Value);

#pragma warning disable CA1508 // Justification: other thread can initialize it
            if (_topicArnMaps == null)
#pragma warning restore CA1508
            {
                _topicArnMaps = new ConcurrentDictionary<string, string>(StringComparer.Ordinal);

                string? nextToken = null;
                do
                {
                    var topics =
                        nextToken == null
                            ? await _snsClient.ListTopicsAsync(cancellationToken).ConfigureAwait(false)
                            : await _snsClient.ListTopicsAsync(nextToken, cancellationToken).ConfigureAwait(false);
                    topics.Topics.ForEach(x =>
                    {
                        var name = x.TopicArn.Split(':')[^1];
                        _topicArnMaps[name] = x.TopicArn;
                    });
                    nextToken = topics.NextToken;
                } while (!string.IsNullOrEmpty(nextToken));
            }
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private async Task<(bool success, string? topicArn)> _TryGetOrCreateTopicArnAsync(
        string topicName,
        CancellationToken cancellationToken = default
    )
    {
        if (_topicArnMaps!.TryGetValue(topicName, out var topicArn))
        {
            return (true, topicArn);
        }

        var response = topicName.IsAwsFifoName()
            ? await _snsClient!
                .CreateTopicAsync(topicName.ToSnsCreateTopicRequest(), cancellationToken)
                .ConfigureAwait(false)
            : await _snsClient!.CreateTopicAsync(topicName, cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrEmpty(response.TopicArn))
        {
            return (false, null);
        }

        // TryAdd is thread-safe and returns false if key exists (handles race condition)
        _topicArnMaps.TryAdd(topicName, response.TopicArn);

        // Get the actual value from dict in case another thread won the race
        topicArn = _topicArnMaps[topicName];
        return (true, topicArn);
    }

    public async ValueTask DisposeAsync()
    {
        await castAndDispose(_semaphore).ConfigureAwait(false);

        if (_snsClient is not null)
        {
            await castAndDispose(_snsClient).ConfigureAwait(false);
        }

        _topicArnMaps = null;

        return;

        static ValueTask castAndDispose(IDisposable resource)
        {
            if (resource is IAsyncDisposable resourceAsyncDisposable)
            {
                return resourceAsyncDisposable.DisposeAsync();
            }

            resource.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private string _GetBrokerEndpoint()
    {
        var options = sqsOptionsAccessor.Value;
        return AwsBrokerEndpoint.Resolve(options.SnsServiceUrl, "sns", options);
    }
}

internal static partial class AmazonSnsBusTransportLog
{
    [LoggerMessage(
        EventId = 1,
        EventName = "SnsTopicMessagePublished",
        Level = LogLevel.Debug,
        Message = "SNS topic message [{NormalizeForAws}] has been published."
    )]
    public static partial void LogSnsTopicMessagePublished(this ILogger logger, string normalizeForAws);

    [LoggerMessage(
        EventId = 2,
        EventName = "SnsTopicNotFound",
        Level = LogLevel.Warning,
        Message = "Can't be found SNS topics for [{NormalizeForAws}]"
    )]
    public static partial void LogSnsTopicNotFound(this ILogger logger, string normalizeForAws);
}
