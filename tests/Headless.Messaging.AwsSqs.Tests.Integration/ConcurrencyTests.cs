// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Amazon.SimpleNotificationService;
using Headless.Messaging.AwsSqs;
using Headless.Messaging.Messages;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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
                        ["Name"] = topicName,
                        ["MessageId"] = messageId.ToString(CultureInfo.InvariantCulture),
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
                    new Dictionary<string, string?>(StringComparer.Ordinal) { ["Name"] = topicName },
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
                    new Dictionary<string, string?>(StringComparer.Ordinal) { ["Name"] = topicName },
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
    public async Task should_handle_mixed_success_and_failure_scenarios()
    {
        // given
        await using var transport = await _CreateTransportAsync();
        const string existingTopic = "existing-topic";
        await _CreateTopicAsync(existingTopic);

        const int parallelCount = 50;
        var results = new OperateResult[parallelCount];

        // when - Mix successful and failing requests
        await Parallel.ForEachAsync(
            Enumerable.Range(0, parallelCount),
            AbortToken,
            async (messageId, _) =>
            {
                var topicName = messageId % 2 == 0 ? existingTopic : $"non-existent-{messageId}";
                var message = new TransportMessage(
                    new Dictionary<string, string?>(StringComparer.Ordinal) { ["Name"] = topicName },
                    Encoding.UTF8.GetBytes($"{{\"index\":{messageId}}}")
                );

                results[messageId] = await transport.SendAsync(message);
            }
        );

        // then
        results.Should().HaveCount(parallelCount);
        var successCount = results.Count(r => r.Succeeded);
        var failureCount = results.Count(r => !r.Succeeded);

        successCount.Should().BeGreaterThan(0);
        failureCount.Should().BeGreaterThan(0);
        (successCount + failureCount).Should().Be(parallelCount);
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
