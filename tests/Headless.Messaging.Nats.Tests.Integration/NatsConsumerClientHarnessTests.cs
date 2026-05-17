// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Nats;
using Headless.Messaging.Transport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tests;

[Collection("Nats")]
public sealed class NatsConsumerClientHarnessTests(NatsFixture fixture) : ConsumerClientTestsBase
{
    private readonly IServiceProvider _serviceProvider = new ServiceCollection().BuildServiceProvider();

    // Each instance (= each test invocation) gets a unique topic prefix to avoid stream/subject
    // collisions across tests that run in parallel under the same NATS fixture.
    private readonly string _topicPrefix = $"h{Guid.NewGuid():N}";

    protected override async ValueTask DisposeAsyncCore()
    {
        if (_serviceProvider is IAsyncDisposable disposable)
        {
            await disposable.DisposeAsync();
        }

        await base.DisposeAsyncCore();
    }

    protected override async Task<IConsumerClient> GetConsumerClientAsync()
    {
        var client = new NatsConsumerClient(
            "test-group",
            1,
            Options.Create(
                new MessagingNatsOptions
                {
                    Servers = fixture.ConnectionString,
                    EnableSubscriberClientStreamAndSubjectCreation = true,
                }
            ),
            _serviceProvider
        );

        await client.ConnectAsync();
        return client;
    }

    /// <inheritdoc />
    /// <remarks>
    /// JetStream consumers must reference an existing stream, and stream subjects cannot overlap
    /// across streams. Two transformations happen here:
    /// <list type="number">
    ///   <item>Each logical topic is namespaced under the test-instance prefix using a hierarchical
    ///     dot-separated subject (<c>{prefix}.{topic}</c>). The dot keeps the resulting JetStream
    ///     subject a multi-token name (<c>{prefix}.&gt;</c>) so it never overlaps with the
    ///     single-token <c>*</c> wildcard used by neighbouring test fixtures.</item>
    ///   <item><c>FetchTopicsAsync</c> (gated behind <c>EnableSubscriberClientStreamAndSubjectCreation</c>)
    ///     materialises the stream + subject bindings before <c>SubscribeAsync</c>.</item>
    /// </list>
    /// Without these, <c>ListeningAsync</c> would spin forever in <c>CreateOrUpdateConsumerAsync</c>
    /// retry loops against a missing or conflicting stream.
    /// </remarks>
    protected override async ValueTask<IReadOnlyList<string>> ResolveSubscriptionTopicsAsync(
        IConsumerClient consumer,
        IReadOnlyList<string> topics
    )
    {
        var prefixed = topics.Select(t => $"{_topicPrefix}.{t}").ToList();
        await consumer.FetchTopicsAsync(prefixed);
        return prefixed;
    }

    [Fact]
    public override Task should_subscribe_to_topic() => base.should_subscribe_to_topic();

    [Fact]
    public override Task should_receive_messages_via_listen_callback() =>
        base.should_receive_messages_via_listen_callback();

    [Fact]
    public override Task should_commit_message_successfully() => base.should_commit_message_successfully();

    [Fact]
    public override Task should_reject_message_successfully() => base.should_reject_message_successfully();

    [Fact]
    public override async Task should_fetch_topics()
    {
        // The base test invokes FetchTopicsAsync with a hardcoded set of single-token topic names
        // (e.g., "topic-1"). NATS rejects those when a neighbouring test fixture has already
        // created a stream with subject "*" because single-token subjects overlap with "*".
        // We mirror the base assertions but prefix the topics with the test-instance namespace
        // so the resulting JetStream subjects are multi-token ({prefix}.topic-1) and never
        // overlap with single-token wildcards used elsewhere.
        await using var consumer = await GetConsumerClientAsync();
        var requestedTopics = new[] { $"{_topicPrefix}.topic-1", $"{_topicPrefix}.topic-2", $"{_topicPrefix}.topic-3" };

        var result = await consumer.FetchTopicsAsync(requestedTopics);

        result.Should().NotBeNull();
        result.Should().HaveCountGreaterThanOrEqualTo(0);
    }

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
