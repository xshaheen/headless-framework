// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Amazon.SimpleNotificationService;
using Headless.Messaging.AwsSqs;
using Headless.Messaging.Messages;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MessagingHeaders = Headless.Messaging.Messages.Headers;
using StringComparer = System.StringComparer;

namespace Tests;

// ReSharper disable AccessToDisposedClosure
[Collection<LocalStackTestFixture>]
public sealed class ConcurrencyTests(LocalStackTestFixture fixture) : TestBase
{
    [Fact]
    public async Task should_handle_parallel_sends_without_race_conditions()
    {
        // given
        const string topicName = "concurrent-test-topic";
        await using var transport = await _CreateTransportAsync();
        await _CreateTopicAsync(topicName);

        const int parallelCount = 100;
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

                results[messageId] = await transport.SendAsync(message);
            }
        );

        // then
        results.Should().HaveCount(parallelCount);
        results.Should().AllSatisfy(r => r.Succeeded.Should().BeTrue());
    }

    [Fact]
    public async Task should_initialize_connection_only_once_with_concurrent_requests()
    {
        // given
        const string topicName = "init-test-topic";
        await using var transport = await _CreateTransportAsync();
        await _CreateTopicAsync(topicName);

        const int parallelCount = 50;
        var results = new OperateResult[parallelCount];

        // when - Send multiple messages immediately to test initialization race condition
        await Parallel.ForEachAsync(
            Enumerable.Range(0, parallelCount),
            AbortToken,
            async (messageId, _) =>
            {
                var message = new TransportMessage(
                    new Dictionary<string, string?>(StringComparer.Ordinal)
                    {
                        [MessagingHeaders.MessageName] = topicName,
                    },
                    "{\"data\":\"init-test\"}"u8.ToArray()
                );

                results[messageId] = await transport.SendAsync(message);
            }
        );

        // then
        results.Should().HaveCount(parallelCount);
        results.Should().AllSatisfy(r => r.Succeeded.Should().BeTrue());
    }

    [Fact]
    public async Task should_handle_concurrent_sends_to_different_topics()
    {
        // given
        await using var transport = await _CreateTransportAsync();
        var topicNames = new[] { "topic-1", "topic-2", "topic-3", "topic-4", "topic-5" };

        foreach (var topicName in topicNames)
        {
            await _CreateTopicAsync(topicName);
        }

        const int parallelCount = 100;
        var results = new OperateResult[parallelCount];

        // when
        await Parallel.ForEachAsync(
            Enumerable.Range(0, parallelCount),
            AbortToken,
            async (messageId, _) =>
            {
                var topicName = topicNames[messageId % topicNames.Length];
                var message = new TransportMessage(
                    new Dictionary<string, string?>(StringComparer.Ordinal)
                    {
                        [MessagingHeaders.MessageName] = topicName,
                    },
                    Encoding.UTF8.GetBytes($"{{\"topic\":\"{topicName}\",\"index\":{messageId}}}")
                );

                results[messageId] = await transport.SendAsync(message);
            }
        );

        // then
        results.Should().HaveCount(parallelCount);
        results.Should().AllSatisfy(r => r.Succeeded.Should().BeTrue());
    }

    [Fact]
    public async Task should_auto_create_topics_for_all_messages()
    {
        // given - The transport auto-creates topics, so all messages should succeed
        await using var transport = await _CreateTransportAsync();

        const int parallelCount = 50;
        var results = new OperateResult[parallelCount];

        // when - Send messages to different topics (some pre-existing, some new)
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

                results[messageId] = await transport.SendAsync(message);
            }
        );

        // then - All should succeed since topics are auto-created
        results.Should().HaveCount(parallelCount);
        results.Should().AllSatisfy(r => r.Succeeded.Should().BeTrue());
    }

    private async Task<ITransport> _CreateTransportAsync()
    {
        var container = fixture.Container;

        var options = Options.Create(
            new AmazonSqsOptions
            {
                Region = Amazon.RegionEndpoint.USEast1,
                SnsServiceUrl = container.GetConnectionString(),
                SqsServiceUrl = container.GetConnectionString(),
                Credentials = new Amazon.Runtime.BasicAWSCredentials("test", "test"),
            }
        );

        var logger = new ServiceCollection()
            .AddLogging(builder => builder.AddConsole())
            .BuildServiceProvider()
            .GetRequiredService<ILogger<AmazonSqsTransport>>();

        var transport = new AmazonSqsTransport(logger, options);

        await Task.Delay(100, AbortToken); // Allow container to stabilize

        return transport;
    }

    private async Task _CreateTopicAsync(string topicName)
    {
        using var snsClient = new AmazonSimpleNotificationServiceClient(
            new Amazon.Runtime.BasicAWSCredentials("test", "test"),
            new AmazonSimpleNotificationServiceConfig { ServiceURL = fixture.Container.GetConnectionString() }
        );

        await snsClient.CreateTopicAsync(topicName.NormalizeForAws(), AbortToken);
    }
}
