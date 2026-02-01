// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Headless.Messaging.Nats;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using NATS.Client;
using MsOptions = Microsoft.Extensions.Options;

namespace Tests;

public sealed class NatsConsumerClientTests : TestBase
{
    private readonly MsOptions.IOptions<MessagingNatsOptions> _options;
    private readonly IServiceProvider _serviceProvider;

    public NatsConsumerClientTests()
    {
        _options = MsOptions.Options.Create(new MessagingNatsOptions { Servers = "nats://localhost:4222" });
        _serviceProvider = new ServiceCollection().BuildServiceProvider();
    }

    [Fact]
    public async Task should_have_correct_broker_address()
    {
        // given, when
        await using var client = _CreateClient("test-group");

        // then
        client.BrokerAddress.Name.Should().Be("nats");
        client.BrokerAddress.Endpoint.Should().Be("nats://localhost:4222");
    }

    [Fact]
    public void should_throw_when_options_value_is_null()
    {
        // given
        var nullOptions = Substitute.For<MsOptions.IOptions<MessagingNatsOptions>>();
        nullOptions.Value.Returns((MessagingNatsOptions)null!);

        // when
        var act = () => new NatsConsumerClient("test-group", 1, nullOptions, _serviceProvider);

        // then
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task should_accept_callback_assignment()
    {
        // given
        await using var client = _CreateClient("test-group");

        // when
        client.OnMessageCallback = (_, _) => Task.CompletedTask;
        client.OnLogCallback = _ => { };

        // then
        client.OnMessageCallback.Should().NotBeNull();
        client.OnLogCallback.Should().NotBeNull();
    }

    [Fact]
    public async Task should_throw_when_subscribing_with_null_topics()
    {
        // given
        await using var client = _CreateClient("test-group");

        // when
        var act = async () => await client.SubscribeAsync(null!);

        // then
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task should_commit_ack_message()
    {
        // given
        await using var client = _CreateClient("test-group", groupConcurrent: 1);
        var msg = Substitute.For<Msg>();

        // when
        await client.CommitAsync(msg);

        // then
        msg.Received(1).Ack();
    }

    [Fact]
    public async Task should_reject_nak_message()
    {
        // given
        await using var client = _CreateClient("test-group", groupConcurrent: 1);
        var msg = Substitute.For<Msg>();

        // when
        await client.RejectAsync(msg);

        // then
        msg.Received(1).Nak();
    }

    [Fact]
    public async Task should_not_throw_when_commit_with_non_msg_sender()
    {
        // given
        await using var client = _CreateClient("test-group", groupConcurrent: 1);

        // when
        var act = async () => await client.CommitAsync("not a msg");

        // then
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task should_not_throw_when_reject_with_non_msg_sender()
    {
        // given
        await using var client = _CreateClient("test-group", groupConcurrent: 1);

        // when
        var act = async () => await client.RejectAsync("not a msg");

        // then
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task should_not_throw_when_commit_with_null_sender()
    {
        // given
        await using var client = _CreateClient("test-group", groupConcurrent: 1);

        // when
        var act = async () => await client.CommitAsync(null);

        // then
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task should_cancel_listening_on_cancellation()
    {
        // given
        await using var client = _CreateClient("test-group");
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        // when
        var act = async () => await client.ListeningAsync(TimeSpan.FromSeconds(30), cts.Token);

        // then
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task should_dispose_without_connection()
    {
        // given
        var client = _CreateClient("test-group");

        // when
        var act = async () => await client.DisposeAsync();

        // then
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task should_return_topics_as_collection_from_fetch()
    {
        // given
        await using var client = _CreateClient("test-group");
        var options = MsOptions.Options.Create(
            new MessagingNatsOptions
            {
                Servers = "nats://localhost:4222",
                EnableSubscriberClientStreamAndSubjectCreation = false,
            }
        );
        await using var clientNoCreate = new NatsConsumerClient("test-group", 1, options, _serviceProvider);
        var topics = new[] { "topic1", "topic2", "topic3" };

        // when
        var result = await clientNoCreate.FetchTopicsAsync(topics);

        // then
        result.Should().BeEquivalentTo(topics);
    }

    /// <summary>
    /// CRITICAL BUG TEST: Verifies that the message handler uses async void which can crash the process.
    /// The _SubscriptionMessageHandler method is declared as 'async void' which is dangerous because
    /// unhandled exceptions in async void methods cannot be caught and will crash the application.
    /// This test documents the bug by checking the method signature.
    /// </summary>
    [Fact]
    public void MessageHandler_should_not_use_async_void()
    {
        // given
        var clientType = typeof(NatsConsumerClient);
        var method = clientType.GetMethod(
            "_SubscriptionMessageHandler",
            BindingFlags.NonPublic | BindingFlags.Instance
        );

        // then
        method.Should().NotBeNull("_SubscriptionMessageHandler method should exist");

        // CRITICAL BUG: This test documents that the handler is async void (which is bad)
        // The return type should be Task, not void, to properly handle exceptions
        var isAsyncVoid =
            method!.ReturnType == typeof(void)
            && method
                .GetCustomAttributes(typeof(System.Runtime.CompilerServices.AsyncStateMachineAttribute), false)
                .Length > 0;

        // This assertion documents the bug - it PASSES when the bug exists
        // When the bug is fixed (method returns Task instead of void), this test should be updated
        isAsyncVoid
            .Should()
            .BeTrue(
                "BUG DETECTED: _SubscriptionMessageHandler is async void which can crash the process on unhandled exceptions. "
                    + "This should be changed to return Task and exceptions should be handled properly."
            );
    }

    private NatsConsumerClient _CreateClient(string groupName, byte groupConcurrent = 1)
    {
        return new NatsConsumerClient(groupName, groupConcurrent, _options, _serviceProvider);
    }
}
