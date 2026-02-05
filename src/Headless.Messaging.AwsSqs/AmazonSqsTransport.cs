// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Messaging.Transport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.AwsSqs;

internal sealed class AmazonSqsTransport(
    ILogger<AmazonSqsTransport> logger,
    IOptions<AmazonSqsOptions> sqsOptionsAccessor
) : ITransport
{
    private readonly ILogger _logger = logger;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private IAmazonSimpleNotificationService? _snsClient;
    private ConcurrentDictionary<string, string>? _topicArnMaps;

    public BrokerAddress BrokerAddress => new("AmazonSQS", string.Empty);

    public async Task<OperateResult> SendAsync(TransportMessage message, CancellationToken cancellationToken = default)
    {
        try
        {
            await _FetchExistingTopicArns(cancellationToken);

            var normalizeForAws = message.GetName().NormalizeForAws();
            var (success, arn) = await _TryGetOrCreateTopicArnAsync(normalizeForAws, cancellationToken);

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

                await _snsClient!.PublishAsync(request, cancellationToken);

                _logger.LogDebug("SNS topic message [{NormalizeForAws}] has been published.", normalizeForAws);

                return OperateResult.Success;
            }

            _logger.LogWarning("Can't be found SNS topics for [{NormalizeForAws}]", normalizeForAws);

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

    private async Task _FetchExistingTopicArns(CancellationToken cancellationToken = default)
    {
        if (_topicArnMaps != null)
        {
            return;
        }

        await _semaphore.WaitAsync(cancellationToken);

        try
        {
            _snsClient = AwsClientFactory.CreateSnsClient(sqsOptionsAccessor.Value);

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
                            ? await _snsClient.ListTopicsAsync(cancellationToken)
                            : await _snsClient.ListTopicsAsync(nextToken, cancellationToken);
                    topics.Topics.ForEach(x =>
                    {
                        var name = x.TopicArn.Split(':')[^1];
                        _topicArnMaps[name] = x.TopicArn;
                    });
                    nextToken = topics.NextToken;
                } while (!string.IsNullOrEmpty(nextToken));
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Init topics from aws sns error!");
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

        var response = await _snsClient!.CreateTopicAsync(topicName, cancellationToken).ConfigureAwait(false);

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
        await castAndDispose(_semaphore);

        if (_snsClient is not null)
        {
            await castAndDispose(_snsClient);
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
}
