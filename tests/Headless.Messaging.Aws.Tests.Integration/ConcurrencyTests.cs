// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Amazon.SimpleNotificationService;
using Headless.Messaging;
using Headless.Messaging.Aws;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MessagingHeaders = Headless.Messaging.Headers;
using StringComparer = System.StringComparer;

namespace Tests;

[Collection<LocalStackTestFixture>]
public sealed class ConcurrencyTests(LocalStackTestFixture fixture) : TestBase
{
    [Fact]
    public async Task should_handle_parallel_sends_without_race_conditions()
    {
        // given
        const string topicName = "concurrent-test-topic";
        await using var transport = _CreateTransport();
        await _CreateTopicAsync(topicName);

        const int parallelCount = 20;
        var results = new OperateResult[parallelCount];

        // when
        await Parallel.ForEachAsync(
            Enumerable.Range(0, parallelCount),
            AbortToken,
            async (messageId, _) =>
            {
                var message = new TransportMessage(
                    new Dictionary<string, string?>(StringComparer.Ordinal)
                    {
                        [MessagingHeaders.MessageName] = topicName,
                        [MessagingHeaders.MessageId] = messageId.ToString(CultureInfo.InvariantCulture),
                    },
                    Encoding.UTF8.GetBytes($"{{\"id\":{messageId}}}")
                );

                results[messageId] = await transport.SendAsync(message, AbortToken);
            }
        );

        // then
        results.Should().HaveCount(parallelCount);
        results.Should().AllSatisfy(r => r.Succeeded.Should().BeTrue("the send failed with {0}", r.Exception));
    }

    [Fact]
    public async Task should_publish_to_many_provisioned_topics_concurrently()
    {
        // given - the deployment created every topic; the publisher only looks each up once
        await using var transport = _CreateTransport();

        for (var topic = 0; topic < 10; topic++)
        {
            await _CreateTopicAsync($"auto-topic-{topic}");
        }

        const int parallelCount = 20;
        var results = new OperateResult[parallelCount];

        // when - send to ten topics at once, two messages each
        await Parallel.ForEachAsync(
            Enumerable.Range(0, parallelCount),
            AbortToken,
            async (messageId, _) =>
            {
                var topicName = $"auto-topic-{messageId % 10}";
                var message = new TransportMessage(
                    new Dictionary<string, string?>(StringComparer.Ordinal)
                    {
                        [MessagingHeaders.MessageName] = topicName,
                    },
                    Encoding.UTF8.GetBytes($"{{\"index\":{messageId}}}")
                );

                results[messageId] = await transport.SendAsync(message, AbortToken);
            }
        );

        // then
        results.Should().HaveCount(parallelCount);
        results.Should().AllSatisfy(r => r.Succeeded.Should().BeTrue("the send failed with {0}", r.Exception));
    }

    private IBusTransport _CreateTransport()
    {
        var container = fixture.Container;

        var options = Options.Create(
            new AmazonSqsMessagingOptions
            {
                Region = Amazon.RegionEndpoint.USEast1,
                SnsServiceUrl = container.GetConnectionString(),
                SqsServiceUrl = container.GetConnectionString(),
                Credentials = new Amazon.Runtime.BasicAWSCredentials("test", "test"),
            }
        );

        return new AmazonSnsBusTransport(NullLogger<AmazonSnsBusTransport>.Instance, options);
    }

    private async Task _CreateTopicAsync(string topicName)
    {
        using var snsClient = new AmazonSimpleNotificationServiceClient(
            new Amazon.Runtime.BasicAWSCredentials("test", "test"),
            new AmazonSimpleNotificationServiceConfig { ServiceURL = fixture.Container.GetConnectionString() }
        );

        await snsClient.CreateTopicAsync(AwsPhysicalAddress.BusTopic("", topicName), AbortToken);
    }
}
