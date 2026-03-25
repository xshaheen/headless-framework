// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Nats;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using NATS.Client.JetStream;
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
        await using var client = _CreateClient("test-group");
        client.BrokerAddress.Name.Should().Be("nats");
        client.BrokerAddress.Endpoint.Should().Be("nats://localhost:4222");
    }

    [Fact]
    public void should_throw_when_options_value_is_null()
    {
        var nullOptions = Substitute.For<MsOptions.IOptions<MessagingNatsOptions>>();
        nullOptions.Value.Returns((MessagingNatsOptions)null!);

        var act = () => new NatsConsumerClient("test-group", 1, nullOptions, _serviceProvider);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task should_accept_callback_assignment()
    {
        await using var client = _CreateClient("test-group");

        client.OnMessageCallback = (_, _) => Task.CompletedTask;
        client.OnLogCallback = _ => { };

        client.OnMessageCallback.Should().NotBeNull();
        client.OnLogCallback.Should().NotBeNull();
    }

    [Fact]
    public async Task should_throw_when_subscribing_with_null_topics()
    {
        await using var client = _CreateClient("test-group");

        var act = async () => await client.SubscribeAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task should_return_topics_as_collection_from_fetch()
    {
        var options = MsOptions.Options.Create(
            new MessagingNatsOptions
            {
                Servers = "nats://localhost:4222",
                EnableSubscriberClientStreamAndSubjectCreation = false,
            }
        );
        await using var client = new NatsConsumerClient("test-group", 1, options, _serviceProvider);
        var topics = new[] { "topic1", "topic2", "topic3" };

        var result = await client.FetchTopicsAsync(topics);
        result.Should().BeEquivalentTo(topics);
    }

    [Fact]
    public async Task should_dispose_without_connection()
    {
        var client = _CreateClient("test-group");

        var act = async () => await client.DisposeAsync();
        await act.Should().NotThrowAsync();
    }

    // Pause/Resume tests

    [Fact]
    public async Task PauseAsync_is_idempotent_when_called_twice()
    {
        await using var client = _CreateClient("test-group");

        await client.PauseAsync();
        await client.PauseAsync();
    }

    [Fact]
    public async Task ResumeAsync_is_noop_when_not_paused()
    {
        await using var client = _CreateClient("test-group");
        await client.ResumeAsync();
    }

    [Fact]
    public async Task PauseAsync_then_ResumeAsync_completes_full_cycle()
    {
        await using var client = _CreateClient("test-group");

        await client.PauseAsync();
        await client.ResumeAsync();
    }

    [Fact]
    public async Task ResumeAsync_is_idempotent_after_resume()
    {
        await using var client = _CreateClient("test-group");

        await client.PauseAsync();
        await client.ResumeAsync();
        await client.ResumeAsync();
    }

    [Fact]
    public async Task PauseAsync_is_noop_after_disposal()
    {
        var client = _CreateClient("test-group");
        await client.DisposeAsync();

        await client.PauseAsync();
    }

    [Fact]
    public async Task ResumeAsync_is_noop_after_disposal()
    {
        var client = _CreateClient("test-group");
        await client.DisposeAsync();

        await client.ResumeAsync();
    }

    // CommitAsync / RejectAsync tests

    [Fact]
    public async Task CommitAsync_should_ack_valid_nats_message()
    {
        await using var client = _CreateClient("test-group");
        var msg = Substitute.For<INatsJSMsg<ReadOnlyMemory<byte>>>();

        await client.CommitAsync(msg);

        await msg.Received(1).AckAsync(cancellationToken: Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RejectAsync_should_nak_valid_nats_message()
    {
        await using var client = _CreateClient("test-group");
        var msg = Substitute.For<INatsJSMsg<ReadOnlyMemory<byte>>>();

        await client.RejectAsync(msg);

        await msg.Received(1).NakAsync(cancellationToken: Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CommitAsync_should_not_throw_for_null_sender()
    {
        await using var client = _CreateClient("test-group");

        var act = async () => await client.CommitAsync(null);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task RejectAsync_should_not_throw_for_null_sender()
    {
        await using var client = _CreateClient("test-group");

        var act = async () => await client.RejectAsync(null);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task CommitAsync_should_not_throw_for_non_nats_sender()
    {
        await using var client = _CreateClient("test-group");

        var act = async () => await client.CommitAsync("not a nats message");
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task RejectAsync_should_not_throw_for_non_nats_sender()
    {
        await using var client = _CreateClient("test-group");

        var act = async () => await client.RejectAsync("not a nats message");
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task CommitAsync_should_log_on_ack_failure()
    {
        await using var client = _CreateClient("test-group");
        LogMessageEventArgs? loggedArgs = null;
        client.OnLogCallback = args => loggedArgs = args;

        var msg = Substitute.For<INatsJSMsg<ReadOnlyMemory<byte>>>();
        msg.AckAsync(cancellationToken: Arg.Any<CancellationToken>())
            .Returns(x => throw new InvalidOperationException("ack failed"));

        await client.CommitAsync(msg);

        loggedArgs.Should().NotBeNull();
        loggedArgs!.LogType.Should().Be(MqLogType.AsyncErrorEvent);
        loggedArgs.Reason.Should().Contain("ack failed");
    }

    [Fact]
    public async Task RejectAsync_should_log_on_nak_failure()
    {
        await using var client = _CreateClient("test-group");
        LogMessageEventArgs? loggedArgs = null;
        client.OnLogCallback = args => loggedArgs = args;

        var msg = Substitute.For<INatsJSMsg<ReadOnlyMemory<byte>>>();
        msg.NakAsync(cancellationToken: Arg.Any<CancellationToken>())
            .Returns(x => throw new InvalidOperationException("nak failed"));

        await client.RejectAsync(msg);

        loggedArgs.Should().NotBeNull();
        loggedArgs!.LogType.Should().Be(MqLogType.AsyncErrorEvent);
        loggedArgs.Reason.Should().Contain("nak failed");
    }

    private NatsConsumerClient _CreateClient(string groupName, byte groupConcurrent = 1)
    {
        return new NatsConsumerClient(groupName, groupConcurrent, _options, _serviceProvider);
    }
}
