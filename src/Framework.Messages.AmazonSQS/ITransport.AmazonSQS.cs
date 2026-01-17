// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using Framework.Messages.Internal;
using Framework.Messages.Messages;
using Framework.Messages.Transport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Framework.Messages;

internal sealed class AmazonSqsTransport(ILogger<AmazonSqsTransport> logger, IOptions<AmazonSQSOptions> sqsOptions)
    : ITransport
{
    private readonly ILogger _logger = logger;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private IAmazonSimpleNotificationService? _snsClient;
    private IDictionary<string, string>? _topicArnMaps;

    public BrokerAddress BrokerAddress => new("AmazonSQS", string.Empty);

    public async Task<OperateResult> SendAsync(TransportMessage message)
    {
        try
        {
            await _FetchExistingTopicArns();

            if (_TryGetOrCreateTopicArn(message.GetName().NormalizeForAws(), out var arn))
            {
                string? bodyJson = null;
                if (message.Body.Length > 0)
                    bodyJson = Encoding.UTF8.GetString(message.Body.Span);

                var attributes = message
                    .Headers.Where(x => x.Value != null)
                    .ToDictionary(
                        x => x.Key,
                        x => new MessageAttributeValue { StringValue = x.Value, DataType = "String" }
                    );

                var request = new PublishRequest(arn, bodyJson) { MessageAttributes = attributes };

                await _snsClient!.PublishAsync(request);

                _logger.LogDebug($"SNS topic message [{message.GetName().NormalizeForAws()}] has been published.");
                return OperateResult.Success;
            }

            var errorMessage = $"Can't be found SNS topics for [{message.GetName().NormalizeForAws()}]";
            _logger.LogWarning(errorMessage);

            return OperateResult.Failed(
                new PublisherSentFailedException(errorMessage),
                new OperateError
                {
                    Code = "SNS",
                    Description = $"Can't be found SNS topics for [{message.GetName().NormalizeForAws()}]",
                }
            );
        }
        catch (Exception ex)
        {
            var wrapperEx = new PublisherSentFailedException(ex.Message, ex);
            var errors = new OperateError { Code = ex.HResult.ToString(), Description = ex.Message };

            return OperateResult.Failed(wrapperEx, errors);
        }
    }

    private async Task _FetchExistingTopicArns()
    {
        if (_topicArnMaps != null)
            return;

        await _semaphore.WaitAsync();

        try
        {
            if (string.IsNullOrWhiteSpace(sqsOptions.Value.SnsServiceUrl))
                _snsClient =
                    sqsOptions.Value.Credentials != null
                        ? new AmazonSimpleNotificationServiceClient(
                            sqsOptions.Value.Credentials,
                            sqsOptions.Value.Region
                        )
                        : new AmazonSimpleNotificationServiceClient(sqsOptions.Value.Region);
            else
                _snsClient =
                    sqsOptions.Value.Credentials != null
                        ? new AmazonSimpleNotificationServiceClient(
                            sqsOptions.Value.Credentials,
                            new AmazonSimpleNotificationServiceConfig { ServiceURL = sqsOptions.Value.SnsServiceUrl }
                        )
                        : new AmazonSimpleNotificationServiceClient(
                            new AmazonSimpleNotificationServiceConfig { ServiceURL = sqsOptions.Value.SnsServiceUrl }
                        );

            if (_topicArnMaps == null)
            {
                _topicArnMaps = new Dictionary<string, string>();

                string? nextToken = null;
                do
                {
                    var topics =
                        nextToken == null
                            ? await _snsClient.ListTopicsAsync()
                            : await _snsClient.ListTopicsAsync(nextToken);
                    topics.Topics.ForEach(x =>
                    {
                        var name = x.TopicArn.Split(':').Last();
                        _topicArnMaps.Add(name, x.TopicArn);
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

    private bool _TryGetOrCreateTopicArn(string topicName, [NotNullWhen(true)] out string? topicArn)
    {
        topicArn = null;
        if (_topicArnMaps!.TryGetValue(topicName, out topicArn))
            return true;

        var response = _snsClient!.CreateTopicAsync(topicName).GetAwaiter().GetResult();

        if (string.IsNullOrEmpty(response.TopicArn))
            return false;

        topicArn = response.TopicArn;

        _topicArnMaps.Add(topicName, topicArn);
        return true;
    }
}
