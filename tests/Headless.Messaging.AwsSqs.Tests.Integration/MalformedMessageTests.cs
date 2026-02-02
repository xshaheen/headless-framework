// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Amazon.SQS;
using Amazon.SQS.Model;
using Headless.Messaging.AwsSqs;
using Headless.Messaging.Messages;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;
using Meziantou.Extensions.Logging.Xunit.v3;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tests;

[Collection<LocalStackTestFixture>]
public sealed class MalformedMessageTests(LocalStackTestFixture fixture) : TestBase
{
    [Fact]
    public async Task should_reject_message_with_invalid_json()
    {
        // given
        const string groupId = "malformed-json-test-group";
        var queueUrl = await _CreateQueueAsync(groupId);
        using var sqsClient = _CreateSqsClient();

        var receivedMessageCount = 0;
        var consumerClient = await _CreateConsumerClientAsync(groupId);
        consumerClient.OnMessageCallback = (_, _) =>
        {
            receivedMessageCount++;
            return Task.CompletedTask;
        };

        // Send malformed JSON directly to queue
        await sqsClient.SendMessageAsync(
            new SendMessageRequest { QueueUrl = queueUrl, MessageBody = "{invalid json structure" },
            AbortToken
        );

        // when
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        try
        {
            await consumerClient.ListeningAsync(TimeSpan.FromMilliseconds(100), cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Expected timeout
        }

        // then
        receivedMessageCount.Should().Be(0, "malformed JSON should not be delivered to consumer");
    }

    [Fact]
    public async Task should_reject_message_with_null_deserialization()
    {
        // given
        const string groupId = "null-json-test-group";
        var queueUrl = await _CreateQueueAsync(groupId);
        using var sqsClient = _CreateSqsClient();

        var receivedMessageCount = 0;
        var consumerClient = await _CreateConsumerClientAsync(groupId);
        consumerClient.OnMessageCallback = (_, _) =>
        {
            receivedMessageCount++;
            return Task.CompletedTask;
        };

        // Send message that deserializes to null
        await sqsClient.SendMessageAsync(
            new SendMessageRequest { QueueUrl = queueUrl, MessageBody = "null" },
            AbortToken
        );

        // when
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        try
        {
            await consumerClient.ListeningAsync(TimeSpan.FromMilliseconds(100), cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Expected timeout
        }

        // then
        receivedMessageCount.Should().Be(0, "null deserialization should not be delivered to consumer");
    }

    [Fact]
    public async Task should_reject_message_with_missing_message_attributes()
    {
        // given
        const string groupId = "missing-attrs-test-group";
        var queueUrl = await _CreateQueueAsync(groupId);
        using var sqsClient = _CreateSqsClient();

        var receivedMessageCount = 0;
        var consumerClient = await _CreateConsumerClientAsync(groupId);
        consumerClient.OnMessageCallback = (_, _) =>
        {
            receivedMessageCount++;
            return Task.CompletedTask;
        };

        // Send message with missing MessageAttributes field
        await sqsClient.SendMessageAsync(
            new SendMessageRequest { QueueUrl = queueUrl, MessageBody = "{\"Message\":\"test\"}" },
            AbortToken
        );

        // when
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        try
        {
            await consumerClient.ListeningAsync(TimeSpan.FromMilliseconds(100), cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Expected timeout
        }

        // then
        receivedMessageCount.Should().Be(0, "message without MessageAttributes should not be delivered to consumer");
    }

    [Fact]
    public async Task should_handle_well_formed_message_correctly()
    {
        // given
        const string groupId = "valid-message-test-group";
        var queueUrl = await _CreateQueueAsync(groupId);
        using var sqsClient = _CreateSqsClient();

        var receivedMessageCount = 0;
        TransportMessage? receivedMessage = null;
        var consumerClient = await _CreateConsumerClientAsync(groupId);
        consumerClient.OnMessageCallback = async (msg, receiptHandle) =>
        {
            receivedMessageCount++;
            receivedMessage = msg;
            await consumerClient.CommitAsync(receiptHandle);
        };

        // Send well-formed message
        const string validMessage = """
            {
                "Message": "test content",
                "MessageAttributes": {
                    "TestHeader": {"Type": "String", "Value": "TestValue"}
                }
            }
            """;

        await sqsClient.SendMessageAsync(
            new SendMessageRequest { QueueUrl = queueUrl, MessageBody = validMessage },
            AbortToken
        );

        // when
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        try
        {
            await consumerClient.ListeningAsync(TimeSpan.FromMilliseconds(100), cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Expected timeout
        }

        // then
        receivedMessageCount.Should().Be(1);
        receivedMessage.Should().NotBeNull();
        receivedMessage.Value.Headers["TestHeader"].Should().Be("TestValue");
    }

    private async Task<string> _CreateQueueAsync(string queueName)
    {
        using var sqsClient = _CreateSqsClient();
        var response = await sqsClient.CreateQueueAsync(queueName.NormalizeForAws());
        return response.QueueUrl;
    }

    private IAmazonSQS _CreateSqsClient()
    {
        var options = new AmazonSqsOptions
        {
            Region = Amazon.RegionEndpoint.USEast1,
            SqsServiceUrl = fixture.Container.GetConnectionString(),
        };

        return new AmazonSQSClient(
            new Amazon.Runtime.BasicAWSCredentials("test", "test"),
            new AmazonSQSConfig { ServiceURL = options.SqsServiceUrl }
        );
    }

    private async Task<IConsumerClient> _CreateConsumerClientAsync(string groupId)
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddXunit());

        var options = Options.Create(
            new AmazonSqsOptions
            {
                Region = Amazon.RegionEndpoint.USEast1,
                SqsServiceUrl = fixture.Container.GetConnectionString(),
                Credentials = new Amazon.Runtime.BasicAWSCredentials("test", "test"),
            }
        );

        var logger = services.BuildServiceProvider().GetRequiredService<ILogger<AmazonSqsConsumerClient>>();

        var factory = new AmazonSqsConsumerClientFactory(options, logger);
        return await factory.CreateAsync(groupId, 0);
    }
}
