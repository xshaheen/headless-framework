// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Amazon.SimpleNotificationService;
using Headless.Messaging.AwsSqs;
using Headless.Messaging.Transport;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Tests.Capabilities;

namespace Tests;

/// <summary>
/// Integration tests for AWS SQS/SNS transport using real LocalStack container.
/// Inherits from <see cref="TransportTestsBase"/> to run standard transport tests.
/// </summary>
[Collection<LocalStackTestFixture>]
public sealed class AmazonSqsTransportTests(LocalStackTestFixture fixture) : TransportTestsBase
{
    private IAmazonSimpleNotificationService? _snsClient;

    /// <inheritdoc />
    protected override TransportCapabilities Capabilities => new()
    {
        // SQS FIFO supports ordering; standard queues do not guarantee order
        SupportsOrdering = false,
        SupportsDeadLetter = true,
        SupportsPriority = false,
        SupportsDelayedDelivery = true,
        SupportsBatchSend = true,
        SupportsHeaders = true,
    };

    /// <inheritdoc />
    protected override ITransport GetTransport()
    {
        var logger = NullLogger<AmazonSqsTransport>.Instance;
        var options = Options.Create(new AmazonSqsOptions
        {
            Region = Amazon.RegionEndpoint.USEast1,
            SnsServiceUrl = fixture.ConnectionString,
            SqsServiceUrl = fixture.ConnectionString,
        });

        return new AmazonSqsTransport(logger, options);
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
        if (_snsClient is not null)
        {
            _snsClient.Dispose();
            _snsClient = null;
        }

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
    public override Task should_send_message_successfully() => base.should_send_message_successfully();

    [Fact]
    public override Task should_have_valid_broker_address() => base.should_have_valid_broker_address();

    [Fact]
    public override Task should_include_headers_in_sent_message() => base.should_include_headers_in_sent_message();

    [Fact]
    public override Task should_send_batch_of_messages() => base.should_send_batch_of_messages();

    [Fact]
    public override Task should_handle_empty_message_body() => base.should_handle_empty_message_body();

    [Fact]
    public override Task should_handle_large_message_body() => base.should_handle_large_message_body();

    [Fact(Skip = "SQS standard queues do not guarantee message ordering")]
    public override Task should_maintain_message_ordering() => base.should_maintain_message_ordering();

    [Fact]
    public override Task should_dispose_async_without_exception() => base.should_dispose_async_without_exception();

    [Fact]
    public override Task should_handle_concurrent_sends() => base.should_handle_concurrent_sends();

    [Fact]
    public override Task should_include_message_id_in_headers() => base.should_include_message_id_in_headers();

    [Fact]
    public override Task should_include_message_name_in_headers() => base.should_include_message_name_in_headers();

    [Fact]
    public override Task should_handle_special_characters_in_message_body() =>
        base.should_handle_special_characters_in_message_body();

    [Fact]
    public override Task should_handle_null_header_values() => base.should_handle_null_header_values();

    [Fact]
    public override Task should_handle_correlation_id_header() => base.should_handle_correlation_id_header();

    #endregion

    #region SQS-Specific Tests

    [Fact]
    public async Task should_fail_when_topic_not_found()
    {
        // given
        await using var transport = GetTransport();
        var message = CreateMessage(messageName: "non-existent-topic");

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
        await _CreateTopicAsync(topicName);
        await using var transport = GetTransport();

        var additionalHeaders = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["CustomHeader"] = "CustomValue",
        };
        var message = CreateMessage(messageName: topicName, additionalHeaders: additionalHeaders);

        // when
        var result = await transport.SendAsync(message);

        // then
        result.Succeeded.Should().BeTrue();
    }

    #endregion
}
