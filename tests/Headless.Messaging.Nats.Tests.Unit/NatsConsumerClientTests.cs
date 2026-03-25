// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Nats;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
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

    private NatsConsumerClient _CreateClient(string groupName, byte groupConcurrent = 1)
    {
        return new NatsConsumerClient(groupName, groupConcurrent, _options, _serviceProvider);
    }
}
