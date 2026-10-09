// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.Aws;

internal sealed class AmazonSnsBusTransport(
    ILogger<AmazonSnsBusTransport> logger,
    IOptions<AmazonSqsMessagingOptions> sqsOptionsAccessor
) : IBusTransport
{
    private readonly ILogger _logger = logger;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private IAmazonSimpleNotificationService? _snsClient;
    private ConcurrentDictionary<string, string>? _topicArnMaps;

    // One create per topic: concurrent first sends share it instead of each calling CreateTopic for the same name.
    // Lazy starts the create once even when GetOrAdd races; a failed create is removed so a later send retries.
    private readonly ConcurrentDictionary<string, Lazy<Task<string?>>> _topicCreations = new(StringComparer.Ordinal);

    // Set once DisposeAsync runs: a later send fails instead of reaching the broker.
    private int _disposed;

    public BrokerAddress BrokerAddress => new("aws_sns", _GetBrokerEndpoint());

    public async Task<OperateResult> SendAsync(TransportMessage message, CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return OperateResult.Failed(new ObjectDisposedException(nameof(AmazonSnsBusTransport)));
        }

        try
        {
            if (!message.Name.IsAwsFifoName())
            {
                MessagingRoutingAffinityMapping.RejectUnsupported(message, "AWS standard destination");
            }

            var affinityKey = AwsRoutingAffinity.Mapping.ResolveKey(message);
            await _FetchExistingTopicArns(cancellationToken).ConfigureAwait(false);

            var normalizeForAws = AwsPhysicalAddress.BusTopic(message.Name);
            var (success, arn) = await _TryGetOrCreateTopicArnAsync(normalizeForAws, cancellationToken)
                .ConfigureAwait(false);

            if (success)
            {
                // SNS requires a non-null message body; use empty string for empty bodies
                var bodyJson = message.Body.Length > 0 ? Encoding.UTF8.GetString(message.Body.Span) : string.Empty;

                // One header bag instead of an attribute per header: raw message delivery hands SNS attributes to SQS
                // as message attributes, and SQS takes at most ten of them.
                var attributes = SqsHeaderCodec.EncodeSns(message);

                var request = new PublishRequest(arn, bodyJson) { MessageAttributes = attributes };

                if (normalizeForAws.IsAwsFifoName())
                {
                    request.MessageGroupId = _ResolveMessageGroupId(affinityKey);

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

    // A FIFO destination requires a message group; without an affinity key every message shares one ordered group.
    private static string _ResolveMessageGroupId(string? messageGroupId)
    {
        if (!string.IsNullOrWhiteSpace(messageGroupId))
        {
            return messageGroupId;
        }

        return "default";
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

            if (_topicArnMaps == null)
            {
                // Publish the cache only once the listing completes: a sender that sees it skips the lock, so a cache
                // published early sends against a partial listing, and a failed listing would never be retried.
                var topicArnMaps = new ConcurrentDictionary<string, string>(StringComparer.Ordinal);

                string? nextToken = null;
                do
                {
                    var topics =
                        nextToken == null
                            ? await _snsClient.ListTopicsAsync(cancellationToken).ConfigureAwait(false)
                            : await _snsClient.ListTopicsAsync(nextToken, cancellationToken).ConfigureAwait(false);

                    // The SDK leaves Topics null for a page with no topics, such as an account with none yet.
                    foreach (var topic in topics.Topics ?? [])
                    {
                        topicArnMaps[topic.TopicArn.Split(':')[^1]] = topic.TopicArn;
                    }

                    nextToken = topics.NextToken;
                } while (!string.IsNullOrEmpty(nextToken));

                _topicArnMaps = topicArnMaps;
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

        var creation = _topicCreations.GetOrAdd(
            topicName,
            static (name, state) => new Lazy<Task<string?>>(() => state._ResolveTopicAsync(name)),
            this
        );

        try
        {
            // The shared create outlives one caller's cancellation, so a cancelled send stops waiting without
            // failing the other senders that wait on the same create.
            topicArn = await creation.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch when (!cancellationToken.IsCancellationRequested)
        {
            // Drop the failed create so the next send retries instead of rethrowing a cached failure.
            _topicCreations.TryRemove(KeyValuePair.Create(topicName, creation));
            throw;
        }

        if (topicArn is null)
        {
            _topicCreations.TryRemove(KeyValuePair.Create(topicName, creation));
            return (false, null);
        }

        return (true, topicArn);
    }

    // Creates the topic, or with AutoProvision off looks it up again, so a topic created after the listing is found
    // without a restart. CreateTopic is idempotent and returns the ARN of an existing topic.
    private async Task<string?> _ResolveTopicAsync(string topicName)
    {
        string? topicArn;

        if (!sqsOptionsAccessor.Value.AutoProvision)
        {
            var topic = await _snsClient!.FindTopicAsync(topicName).ConfigureAwait(false);
            topicArn = topic?.TopicArn;
        }
        else
        {
            var response = topicName.IsAwsFifoName()
                ? await _snsClient!
                    .CreateTopicAsync(topicName.ToSnsCreateTopicRequest(), CancellationToken.None)
                    .ConfigureAwait(false)
                : await _snsClient!.CreateTopicAsync(topicName, CancellationToken.None).ConfigureAwait(false);
            topicArn = response.TopicArn;
        }

        if (string.IsNullOrEmpty(topicArn))
        {
            return null;
        }

        _topicArnMaps?.TryAdd(topicName, topicArn);

        return topicArn;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

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
