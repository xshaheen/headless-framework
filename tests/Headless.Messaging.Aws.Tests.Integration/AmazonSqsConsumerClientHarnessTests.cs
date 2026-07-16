// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Amazon.Runtime;
using Headless.Messaging.Aws;
using Headless.Messaging.Transport;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Tests.Capabilities;

namespace Tests;

[Collection<LocalStackTestFixture>]
public sealed class AmazonSqsConsumerClientHarnessTests(LocalStackTestFixture fixture) : ConsumerClientTestsBase
{
    protected override ConsumerClientCapabilities Capabilities =>
        new()
        {
            SupportsFetchTopics = true,
            SupportsConcurrentProcessing = true,
            SupportsReject = true,
            SupportsGracefulShutdown = true,
        };

    protected override Task<IConsumerClient> GetConsumerClientAsync()
    {
        return Task.FromResult<IConsumerClient>(
            new AmazonSqsConsumerClient(
                $"consumer-tests-{Guid.NewGuid():N}",
                1,
                Options.Create(
                    new AmazonSqsMessagingOptions
                    {
                        Region = Amazon.RegionEndpoint.USEast1,
                        SnsServiceUrl = fixture.ConnectionString,
                        SqsServiceUrl = fixture.ConnectionString,
                        Credentials = new BasicAWSCredentials("test", "test"),
                    }
                ),
                NullLogger<AmazonSqsConsumerClient>.Instance
            )
        );
    }

    protected override async ValueTask<IReadOnlyList<string>> ResolveSubscriptionTopicsAsync(
        IConsumerClient consumer,
        IReadOnlyList<string> messageNames
    )
    {
        var resolved = await consumer.FetchMessageNamesAsync(messageNames);
        return resolved.ToList();
    }

    [Fact]
    public override Task should_subscribe_to_topic()
    {
        return base.should_subscribe_to_topic();
    }

    [Fact]
    public override Task should_receive_messages_via_listen_callback()
    {
        return base.should_receive_messages_via_listen_callback();
    }

#pragma warning disable xUnit1004 // Test methods should not be skipped
    [Fact(Skip = "SQS commit requires a real receipt handle and initialized queue state.")]
#pragma warning restore xUnit1004
    public override Task should_delegate_commit_callback_value()
    {
        return Task.CompletedTask;
    }

#pragma warning disable xUnit1004 // Test methods should not be skipped
    [Fact(Skip = "SQS reject requires a real receipt handle and initialized queue state.")]
#pragma warning restore xUnit1004
    public override Task should_delegate_reject_callback_value()
    {
        return Task.CompletedTask;
    }

    [Fact]
    public override Task should_fetch_topics()
    {
        return base.should_fetch_topics();
    }

    [Fact]
    public override Task should_shutdown_gracefully()
    {
        return base.should_shutdown_gracefully();
    }

    [Fact]
    public override Task should_handle_concurrent_message_processing()
    {
        return base.should_handle_concurrent_message_processing();
    }

    [Fact]
    public override Task should_dispose_without_exception()
    {
        return base.should_dispose_without_exception();
    }

    [Fact]
    public override Task should_have_valid_broker_address()
    {
        return base.should_have_valid_broker_address();
    }

    [Fact]
    public override Task should_invoke_log_callback_on_events()
    {
        return base.should_invoke_log_callback_on_events();
    }
}
