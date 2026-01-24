// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Amazon.SimpleNotificationService;
using AwesomeAssertions;
using Framework.Testing.Tests;
using Headless.Messaging.AwsSqs;
using Headless.Messaging.Messages;
using Headless.Messaging.Transport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Tests;

[Collection<LocalStackTestFixture>]
public sealed class AmazonSqsTransportTests(LocalStackTestFixture fixture) : TestBase
{
    [Fact]
    public async Task should_send_message_successfully()
    {
        // given
        const string topicName = "test-topic";
        await using var transport = await _CreateTransportAsync();
        await _CreateTopicAsync(topicName);

        var message = new TransportMessage(
            new Dictionary<string, string?>(StringComparer.Ordinal) { ["Name"] = topicName },
            "{\"data\":\"test\"}"u8.ToArray()
        );

        // when
        var result = await transport.SendAsync(message);

        // then
        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task should_fail_when_topic_not_found()
    {
        // given
        const string topicName = "non-existent-topic";
        await using var transport = await _CreateTransportAsync();

        var message = new TransportMessage(
            new Dictionary<string, string?>(StringComparer.Ordinal) { ["Name"] = topicName },
            "{\"data\":\"test\"}"u8.ToArray()
        );

        // when
        var result = await transport.SendAsync(message);

        // then
        result.Succeeded.Should().BeFalse();
        result.Exception.Should().NotBeNull();
    }

    [Fact]
    public async Task should_include_message_attributes()
    {
        // given
        const string topicName = "test-attributes-topic";
        await using var transport = await _CreateTransportAsync();
        await _CreateTopicAsync(topicName);

        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Name"] = topicName,
            ["CustomHeader"] = "CustomValue",
            ["MessageId"] = Guid.NewGuid().ToString(),
        };

        var message = new TransportMessage(headers, "{\"data\":\"test\"}"u8.ToArray());

        // when
        var result = await transport.SendAsync(message);

        // then
        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task should_handle_empty_message_body()
    {
        // given
        const string topicName = "test-empty-body-topic";
        await using var transport = await _CreateTransportAsync();
        await _CreateTopicAsync(topicName);

        var message = new TransportMessage(
            new Dictionary<string, string?>(StringComparer.Ordinal) { ["Name"] = topicName },
            ReadOnlyMemory<byte>.Empty
        );

        // when
        var result = await transport.SendAsync(message);

        // then
        result.Succeeded.Should().BeTrue();
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
