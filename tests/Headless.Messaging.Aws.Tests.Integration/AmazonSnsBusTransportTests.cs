// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Amazon.SimpleNotificationService;
using Headless.Messaging;
using Headless.Messaging.Aws;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Tests.Capabilities;

namespace Tests;

/// <summary>
/// Integration tests for AWS SQS/SNS transport using real LocalStack container.
/// Inherits from <see cref="TransportTestsBase"/> to run standard transport tests.
/// </summary>
[Collection<LocalStackTestFixture>]
public sealed class AmazonSnsBusTransportTests(LocalStackTestFixture fixture) : TransportTestsBase
{
    private IAmazonSimpleNotificationService? _snsClient;

    /// <inheritdoc />
    protected override TransportCapabilities Capabilities =>
        new()
        {
            // SQS FIFO supports ordering; standard queues do not guarantee order
            SupportsOrdering = false,
            SupportsDeadLetter = true,
            SupportsPriority = false,
            SupportsDelayedDelivery = true,
            SupportsBusTransport = true,
            SupportsQueueTransport = false,
            SupportsHeaders = true,
        };

    /// <inheritdoc />
    protected override IBusTransport GetBusTransport()
    {
        var logger = NullLogger<AmazonSnsBusTransport>.Instance;
        var options = Options.Create(
            new AmazonSqsMessagingOptions
            {
                Region = Amazon.RegionEndpoint.USEast1,
                SnsServiceUrl = fixture.ConnectionString,
                SqsServiceUrl = fixture.ConnectionString,
                Credentials = new Amazon.Runtime.BasicAWSCredentials("test", "test"),
            }
        );

        return new AmazonSnsBusTransport(logger, options);
    }

    /// <inheritdoc />
    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();

        // Create SNS client for topic creation
        _snsClient = new AmazonSimpleNotificationServiceClient(
            new Amazon.Runtime.BasicAWSCredentials("test", "test"),
            new AmazonSimpleNotificationServiceConfig { ServiceURL = fixture.ConnectionString }
        );

        // Pre-create the default test topic
        await _snsClient.CreateTopicAsync("TestMessage".NormalizeForAws(), AbortToken);
    }

    /// <inheritdoc />
    protected override async ValueTask DisposeAsyncCore()
    {
        _snsClient?.Dispose();
        _snsClient = null;

        await base.DisposeAsyncCore();
    }

    /// <summary>Creates a topic in LocalStack for testing.</summary>
    private async Task _CreateTopicAsync(string topicName)
    {
        if (_snsClient is null)
        {
            return;
        }

        await _snsClient.CreateTopicAsync(topicName.NormalizeForAws(), AbortToken);
    }

    #region Transport Tests

    [Fact]
    public override Task should_send_message_successfully()
    {
        return base.should_send_message_successfully();
    }

    [Fact]
    public override Task should_have_valid_broker_address()
    {
        return base.should_have_valid_broker_address();
    }

    [Fact]
    public override Task should_accept_message_with_application_headers()
    {
        return base.should_accept_message_with_application_headers();
    }

    [Fact]
    public override Task should_send_multiple_messages_individually()
    {
        return base.should_send_multiple_messages_individually();
    }

    [Fact]
    public override Task should_handle_empty_message_body()
    {
        var support = TransportConformanceManifest.Providers["AWS/LocalStack"].Scenarios[
            TransportConformanceScenario.EmptyBodyDispatch
        ];

        if (support.Status != ConformanceStatus.Supported)
        {
            Assert.Skip($"AWS/LocalStack EmptyBodyDispatch: {support.Status}. {support.Rationale}");
        }

        return base.should_handle_empty_message_body();
    }

    [Fact]
    public override Task should_handle_large_message_body()
    {
        return base.should_handle_large_message_body();
    }

    [Fact]
    public override Task should_dispose_async_without_exception()
    {
        return base.should_dispose_async_without_exception();
    }

    [Fact]
    public override Task should_handle_concurrent_sends()
    {
        return base.should_handle_concurrent_sends();
    }

    [Fact]
    public override Task should_accept_message_with_id()
    {
        return base.should_accept_message_with_id();
    }

    [Fact]
    public override Task should_accept_message_with_name()
    {
        return base.should_accept_message_with_name();
    }

    [Fact]
    public override Task should_handle_special_characters_in_message_body()
    {
        return base.should_handle_special_characters_in_message_body();
    }

    [Fact]
    public override Task should_handle_null_header_values()
    {
        return base.should_handle_null_header_values();
    }

    [Fact]
    public override Task should_handle_correlation_id_header()
    {
        return base.should_handle_correlation_id_header();
    }

    #endregion

    #region SQS-Specific Tests

    [Fact]
    public async Task should_auto_create_topic_when_not_found()
    {
        // given - The transport auto-creates topics if they don't exist
        await using var transport = GetBusTransport();
        var message = CreateMessage(messageName: "auto-created-topic");

        // when
        var result = await transport.SendAsync(message, AbortToken);

        // then - Should succeed because topic is auto-created
        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task should_include_message_attributes()
    {
        // given
        const string topicName = "test-attributes-topic";
        await _CreateTopicAsync(topicName);
        await using var transport = GetBusTransport();

        var additionalHeaders = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["CustomHeader"] = "CustomValue",
        };
        var message = CreateMessage(messageName: topicName, additionalHeaders: additionalHeaders);

        // when
        var result = await transport.SendAsync(message, AbortToken);

        // then
        result.Succeeded.Should().BeTrue();
    }

    #endregion
}
