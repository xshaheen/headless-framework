// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Amazon.Runtime;
using Headless.Messaging.AwsSqs;
using Headless.Messaging.Transport;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Tests;

[Collection<LocalStackTestFixture>]
public sealed class AmazonSqsConsumerClientHarnessTests(LocalStackTestFixture fixture) : ConsumerClientTestsBase
{
    protected override ValueTask<IConsumerClient> GetConsumerClientAsync()
    {
        return ValueTask.FromResult<IConsumerClient>(
            new AmazonSqsConsumerClient(
                $"consumer-tests-{Guid.NewGuid():N}",
                1,
                Options.Create(
                    new AmazonSqsOptions
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
        IReadOnlyList<string> topics
    )
    {
        var resolved = await consumer.FetchTopicsAsync(topics);
        return resolved.ToList();
    }

    [Fact]
    public override Task should_subscribe_to_topic() => base.should_subscribe_to_topic();

    [Fact]
    public override Task should_receive_messages_via_listen_callback() =>
        base.should_receive_messages_via_listen_callback();

    [Fact(Skip = "SQS commit requires a real receipt handle and initialized queue state.")]
    public override Task should_commit_message_successfully() => Task.CompletedTask;

    [Fact(Skip = "SQS reject requires a real receipt handle and initialized queue state.")]
    public override Task should_reject_message_successfully() => Task.CompletedTask;

    [Fact]
    public override Task should_fetch_topics() => base.should_fetch_topics();

    [Fact]
    public override Task should_shutdown_gracefully() => base.should_shutdown_gracefully();

    [Fact]
    public override Task should_handle_concurrent_message_processing() =>
        base.should_handle_concurrent_message_processing();

    [Fact]
    public override Task should_dispose_without_exception() => base.should_dispose_without_exception();

    [Fact]
    public override Task should_have_valid_broker_address() => base.should_have_valid_broker_address();

    [Fact]
    public override Task should_invoke_log_callback_on_events() => base.should_invoke_log_callback_on_events();
}
