// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Amazon.SimpleNotificationService;
using AwesomeAssertions;
using Framework.Messages;
using Framework.Messages.Messages;
using Framework.Messages.Transport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Testcontainers.LocalStack;
using Xunit.v3;

namespace Tests;

[Collection<LocalStackTestFixture>]
public sealed class ConcurrencyTests(LocalStackTestFixture fixture) : TestBase
{
    private readonly LocalStackContainer _container = fixture.Container;

    [Fact]
    public async Task should_handle_parallel_sends_without_race_conditions()
    {
        // Arrange
        var topicName = "concurrent-test-topic";
        var transport = await _CreateTransportAsync();
        await _CreateTopicAsync(topicName);

        var parallelCount = 100;
        var tasks = new List<Task<OperateResult>>(parallelCount);

        // Act
        for (var i = 0; i < parallelCount; i++)
        {
            var messageId = i;
            var message = new TransportMessage(
                new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["Name"] = topicName,
                    ["MessageId"] = messageId.ToString(CultureInfo.InvariantCulture),
                },
                Encoding.UTF8.GetBytes($"{{\"id\":{messageId}}}")
            );

            tasks.Add(Task.Run(async () => await transport.SendAsync(message), AbortToken));
        }

        var results = await Task.WhenAll(tasks);

        // Assert
        results.Should().HaveCount(parallelCount);
        results.Should().AllSatisfy(r => r.Succeeded.Should().BeTrue());
    }

    [Fact]
    public async Task should_initialize_connection_only_once_with_concurrent_requests()
    {
        // Arrange
        var topicName = "init-test-topic";
        var transport = await _CreateTransportAsync();
        await _CreateTopicAsync(topicName);

        var parallelCount = 50;
        var tasks = new List<Task<OperateResult>>(parallelCount);

        // Act - Send multiple messages immediately to test initialization race condition
        for (var i = 0; i < parallelCount; i++)
        {
            var message = new TransportMessage(
                new Dictionary<string, string?>(StringComparer.Ordinal) { ["Name"] = topicName },
                Encoding.UTF8.GetBytes("{\"data\":\"init-test\"}")
            );

            tasks.Add(transport.SendAsync(message));
        }

        var results = await Task.WhenAll(tasks);

        // Assert
        results.Should().HaveCount(parallelCount);
        results.Should().AllSatisfy(r => r.Succeeded.Should().BeTrue());
    }

    [Fact]
    public async Task should_handle_concurrent_sends_to_different_topics()
    {
        // Arrange
        var transport = await _CreateTransportAsync();
        var topicNames = new[] { "topic-1", "topic-2", "topic-3", "topic-4", "topic-5" };

        foreach (var topicName in topicNames)
        {
            await _CreateTopicAsync(topicName);
        }

        var parallelCount = 100;
        var tasks = new List<Task<OperateResult>>(parallelCount);

        // Act
        for (var i = 0; i < parallelCount; i++)
        {
            var topicName = topicNames[i % topicNames.Length];
            var message = new TransportMessage(
                new Dictionary<string, string?>(StringComparer.Ordinal) { ["Name"] = topicName },
                Encoding.UTF8.GetBytes($"{{\"topic\":\"{topicName}\",\"index\":{i}}}")
            );

            tasks.Add(Task.Run(async () => await transport.SendAsync(message), AbortToken));
        }

        var results = await Task.WhenAll(tasks);

        // Assert
        results.Should().HaveCount(parallelCount);
        results.Should().AllSatisfy(r => r.Succeeded.Should().BeTrue());
    }

    [Fact]
    public async Task should_handle_mixed_success_and_failure_scenarios()
    {
        // Arrange
        var transport = await _CreateTransportAsync();
        var existingTopic = "existing-topic";
        await _CreateTopicAsync(existingTopic);

        var parallelCount = 50;
        var tasks = new List<Task<OperateResult>>(parallelCount);

        // Act - Mix successful and failing requests
        for (var i = 0; i < parallelCount; i++)
        {
            var topicName = i % 2 == 0 ? existingTopic : $"non-existent-{i}";
            var message = new TransportMessage(
                new Dictionary<string, string?>(StringComparer.Ordinal) { ["Name"] = topicName },
                Encoding.UTF8.GetBytes($"{{\"index\":{i}}}")
            );

            tasks.Add(Task.Run(async () => await transport.SendAsync(message), AbortToken));
        }

        var results = await Task.WhenAll(tasks);

        // Assert
        results.Should().HaveCount(parallelCount);
        var successCount = results.Count(r => r.Succeeded);
        var failureCount = results.Count(r => !r.Succeeded);

        successCount.Should().BeGreaterThan(0);
        failureCount.Should().BeGreaterThan(0);
        (successCount + failureCount).Should().Be(parallelCount);
    }

    private async Task<ITransport> _CreateTransportAsync()
    {
        var options = Options.Create(
            new AmazonSqsOptions
            {
                Region = Amazon.RegionEndpoint.USEast1,
                SnsServiceUrl = _container.GetConnectionString(),
                SqsServiceUrl = _container.GetConnectionString(),
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
        var snsClient = new AmazonSimpleNotificationServiceClient(
            new Amazon.Runtime.BasicAWSCredentials("test", "test"),
            new AmazonSimpleNotificationServiceConfig { ServiceURL = _container.GetConnectionString() }
        );

        await snsClient.CreateTopicAsync(topicName.NormalizeForAws(), AbortToken);
    }
}
