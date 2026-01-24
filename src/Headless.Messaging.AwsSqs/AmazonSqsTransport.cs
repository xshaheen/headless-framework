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

    public async Task<OperateResult> SendAsync(TransportMessage message)
    {
        try
        {
            await _FetchExistingTopicArns();

            var normalizeForAws = message.GetName().NormalizeForAws();
            var (success, arn) = await _TryGetOrCreateTopicArnAsync(normalizeForAws);

            if (success)
            {
                string? bodyJson = null;
                if (message.Body.Length > 0)
                {
                    bodyJson = Encoding.UTF8.GetString(message.Body.Span);
                }

                var attributes = message
                    .Headers.Where(x => x.Value != null)
                    .ToDictionary(
                        x => x.Key,
                        x => new MessageAttributeValue { StringValue = x.Value, DataType = "String" },
                        StringComparer.Ordinal
                    );

                var request = new PublishRequest(arn, bodyJson) { MessageAttributes = attributes };

                await _snsClient!.PublishAsync(request);

                _logger.LogDebug("SNS topic message [{NormalizeForAws}] has been published.", normalizeForAws);

                return OperateResult.Success;
            }

            _logger.LogWarning("Can't be found SNS topics for [{NormalizeForAws}]", normalizeForAws);

            return OperateResult.Failed(
                new PublisherSentFailedException($"Can't be found SNS topics for [{normalizeForAws}]"),
                new OperateError { Code = "SNS", Description = $"Can't be found SNS topics for [{normalizeForAws}]" }
            );
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

    private async Task _FetchExistingTopicArns()
    {
        if (_topicArnMaps != null)
        {
            return;
        }

        await _semaphore.WaitAsync();

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
                            ? await _snsClient.ListTopicsAsync()
                            : await _snsClient.ListTopicsAsync(nextToken);
                    topics.Topics.ForEach(x =>
                    {
                        var name = x.TopicArn.Split(':')[^1];
                        _topicArnMaps[name] = x.TopicArn;
                    });
                    nextToken = topics.NextToken;
                } while (!string.IsNullOrEmpty(nextToken));
            }
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

    private async Task<(bool success, string? topicArn)> _TryGetOrCreateTopicArnAsync(string topicName)
    {
        if (_topicArnMaps!.TryGetValue(topicName, out var topicArn))
        {
            return (true, topicArn);
        }

        var response = await _snsClient!.CreateTopicAsync(topicName).AnyContext();

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
