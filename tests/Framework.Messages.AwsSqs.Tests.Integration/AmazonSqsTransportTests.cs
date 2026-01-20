// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Amazon.SimpleNotificationService;
using Amazon.SQS;
using Amazon.SQS.Model;
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
public sealed class AmazonSqsTransportTests(LocalStackTestFixture fixture) : TestBase
{
    private readonly LocalStackContainer _container = fixture.Container;

    [Fact]
    public async Task should_send_message_successfully()
    {
        // Arrange
        var topicName = "test-topic";
        var transport = await _CreateTransportAsync();
        await _CreateTopicAsync(topicName);

        var message = new TransportMessage(
            new Dictionary<string, string?>(StringComparer.Ordinal) { ["Name"] = topicName },
            Encoding.UTF8.GetBytes("{\"data\":\"test\"}")
        );

        // Act
        var result = await transport.SendAsync(message);

        // Assert
        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task should_fail_when_topic_not_found()
    {
        // Arrange
        var topicName = "non-existent-topic";
        var transport = await _CreateTransportAsync();

        var message = new TransportMessage(
            new Dictionary<string, string?>(StringComparer.Ordinal) { ["Name"] = topicName },
            Encoding.UTF8.GetBytes("{\"data\":\"test\"}")
        );

        // Act
        var result = await transport.SendAsync(message);

        // Assert
        result.Succeeded.Should().BeFalse();
        result.Errors.Should().NotBeEmpty();
    }

    [Fact]
    public async Task should_include_message_attributes()
    {
        // Arrange
        var topicName = "test-attributes-topic";
        var transport = await _CreateTransportAsync();
        await _CreateTopicAsync(topicName);

        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Name"] = topicName,
            ["CustomHeader"] = "CustomValue",
            ["MessageId"] = Guid.NewGuid().ToString(),
        };

        var message = new TransportMessage(headers, Encoding.UTF8.GetBytes("{\"data\":\"test\"}"));

        // Act
        var result = await transport.SendAsync(message);

        // Assert
        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task should_handle_empty_message_body()
    {
        // Arrange
        var topicName = "test-empty-body-topic";
        var transport = await _CreateTransportAsync();
        await _CreateTopicAsync(topicName);

        var message = new TransportMessage(
            new Dictionary<string, string?>(StringComparer.Ordinal) { ["Name"] = topicName },
            ReadOnlyMemory<byte>.Empty
        );

        // Act
        var result = await transport.SendAsync(message);

        // Assert
        result.Succeeded.Should().BeTrue();
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
