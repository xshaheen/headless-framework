// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Exceptions;
using Headless.Messaging.Nats;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using MessagingHeaders = Headless.Messaging.Headers;

namespace Tests;

[Collection("Nats")]
public sealed class NatsConsumerClientTests(NatsFixture fixture) : TransportConsumerConformanceTestsBase
{
    private readonly IServiceProvider _serviceProvider = new ServiceCollection().BuildServiceProvider();

    protected override string ProviderName => "NATS";

    protected override ValueTask<TransportConsumerConformanceSession> CreateSessionAsync(
        CancellationToken cancellationToken
    )
    {
        return fixture.CreateConformanceSessionAsync(cancellationToken);
    }

    [Fact]
    public override Task should_round_trip_queue_message_body_and_headers()
    {
        return base.should_round_trip_queue_message_body_and_headers();
    }

    [Fact]
    public override Task should_commit_real_delivery_and_prevent_redelivery()
    {
        return base.should_commit_real_delivery_and_prevent_redelivery();
    }

    [Fact]
    public override Task should_reject_real_delivery_and_observe_redelivery()
    {
        return base.should_reject_real_delivery_and_observe_redelivery();
    }

    [Fact]
    public override Task should_isolate_unique_destinations()
    {
        return base.should_isolate_unique_destinations();
    }

    [Fact]
    public override Task should_shutdown_idle_consumer_within_bound()
    {
        return base.should_shutdown_idle_consumer_within_bound();
    }

    [Fact]
    public override Task should_bound_shutdown_while_handler_is_active()
    {
        return base.should_bound_shutdown_while_handler_is_active();
    }

    [Fact]
    public async Task should_accept_real_delivery_value_for_commit_callback()
    {
        // given
        var streamName = $"consume-commit-{Guid.NewGuid():N}"[..30];
        var subject = $"{streamName}.test";
        await _EnsureStreamAsync(streamName, $"{streamName}.>");

        var options = _CreateOptions(enableStreamCreation: false);
        await using var client = new NatsConsumerClient("test-group", 0, options, _serviceProvider);
        await client.ConnectAsync(AbortToken);

        var topics = await client.FetchMessageNamesAsync([subject], AbortToken);
        await client.SubscribeAsync(topics, AbortToken);

        var received = new TaskCompletionSource<(TransportMessage msg, object? sender)>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        client.OnMessageCallback = (msg, sender) =>
        {
            received.TrySetResult((msg, sender));
            return Task.CompletedTask;
        };
        client.OnLogCallback = _ => { };

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        // when — start listening, then publish
        var listeningTask = client.ListeningAsync(TimeSpan.FromSeconds(1), cts.Token).AsTask();
        try
        {
            await Task.Delay(500, AbortToken);

            var body = "hello-commit"u8.ToArray();
            await _PublishAsync(subject, body);

            var (transportMsg, natsMsg) = await received.Task.WaitAsync(cts.Token);

            // then — message received with correct body
            transportMsg.Body.ToArray().Should().BeEquivalentTo(body);
            transportMsg.Headers[MessagingHeaders.Group].Should().Be("test-group");

            // commit should not throw
            await client.CommitAsync(natsMsg, AbortToken);
        }
        finally
        {
            await _StopListeningAsync(listeningTask, cts);
        }
    }

    [Fact]
    public async Task should_accept_real_delivery_value_for_reject_callback()
    {
        // given
        var streamName = $"consume-reject-{Guid.NewGuid():N}"[..30];
        var subject = $"{streamName}.test";
        await _EnsureStreamAsync(streamName, $"{streamName}.>");

        var options = _CreateOptions(enableStreamCreation: false);
        await using var client = new NatsConsumerClient("test-group", 0, options, _serviceProvider);
        await client.ConnectAsync(AbortToken);

        await client.FetchMessageNamesAsync([subject], AbortToken);
        await client.SubscribeAsync([subject], AbortToken);

        var received = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.OnMessageCallback = (_, sender) =>
        {
            received.TrySetResult(sender);
            return Task.CompletedTask;
        };
        client.OnLogCallback = _ => { };

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        // when — start listening, then publish
        var listeningTask = client.ListeningAsync(TimeSpan.FromSeconds(1), cts.Token).AsTask();
        try
        {
            await Task.Delay(500, AbortToken);
            await _PublishAsync(subject, "hello-reject"u8.ToArray());

            var natsMsg = await received.Task.WaitAsync(cts.Token);

            // then — reject (nak) should not throw
            await client.RejectAsync(natsMsg, AbortToken);
        }
        finally
        {
            await _StopListeningAsync(listeningTask, cts);
        }
    }

    [Fact]
    public async Task should_create_stream_when_fetch_message_names_async_enabled()
    {
        // given
        var streamName = $"autocreate-{Guid.NewGuid():N}"[..25];
        var subject = $"{streamName}.orders";

        var options = _CreateOptions(enableStreamCreation: true);
        await using var client = new NatsConsumerClient("test-group", 0, options, _serviceProvider);
        await client.ConnectAsync(AbortToken);

        // when — FetchMessageNamesAsync with EnableSubscriberClientStreamAndSubjectCreation=true
        var result = await client.FetchMessageNamesAsync([subject], AbortToken);

        // then — stream should exist on the NATS server
        result.Should().Contain(subject);

        var conn = await fixture.GetConnectionAsync();
        var js = new NatsJSContext(conn);
        var stream = await js.GetStreamAsync(streamName, cancellationToken: AbortToken);
        stream.Should().NotBeNull();
    }

    [Fact]
    public async Task should_apply_stream_options_callback_when_fetch_message_names_async()
    {
        // given
        var streamName = $"stropts-{Guid.NewGuid():N}"[..22];
        var subject = $"{streamName}.events";

        var opts = Options.Create(
            new NatsMessagingOptions
            {
                Servers = fixture.ConnectionString,
                EnableSubscriberClientStreamAndSubjectCreation = true,
                StreamOptions = config => config.Storage = StreamConfigStorage.Memory,
            }
        );

        await using var client = new NatsConsumerClient("test-group", 0, opts, _serviceProvider);
        await client.ConnectAsync(AbortToken);

        // when
        await client.FetchMessageNamesAsync([subject], AbortToken);

        // then — stream should use Memory storage (from callback)
        var conn = await fixture.GetConnectionAsync();
        var js = new NatsJSContext(conn);
        var stream = await js.GetStreamAsync(streamName, cancellationToken: AbortToken);
        var info = stream.Info;
        info.Config.Storage.Should().Be(StreamConfigStorage.Memory);
    }

    [Fact]
    public async Task should_receive_headers_from_published_message()
    {
        // given
        var streamName = $"headers-{Guid.NewGuid():N}"[..22];
        var subject = $"{streamName}.test";
        await _EnsureStreamAsync(streamName, $"{streamName}.>");

        var options = _CreateOptions(enableStreamCreation: false);
        await using var client = new NatsConsumerClient("test-group", 0, options, _serviceProvider);
        await client.ConnectAsync(AbortToken);
        await client.FetchMessageNamesAsync([subject], AbortToken);
        await client.SubscribeAsync([subject], AbortToken);

        var received = new TaskCompletionSource<TransportMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.OnMessageCallback = (msg, _) =>
        {
            received.TrySetResult(msg);
            return Task.CompletedTask;
        };
        client.OnLogCallback = _ => { };

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        // when — start listening, then publish with headers
        var listeningTask = client.ListeningAsync(TimeSpan.FromSeconds(1), cts.Token).AsTask();
        try
        {
            await Task.Delay(500, AbortToken);

            var conn = await fixture.GetConnectionAsync();
            var js = new NatsJSContext(conn);
            var headers = new NatsHeaders { { "X-Custom", "custom-value" } };
            await js.PublishAsync(
                subject,
                "body"u8.ToArray(),
                serializer: NatsRawSerializer<ReadOnlyMemory<byte>>.Default,
                headers: headers,
                cancellationToken: AbortToken
            );

            var transportMsg = await received.Task.WaitAsync(cts.Token);

            // then
            transportMsg.Headers["X-Custom"].Should().Be("custom-value");
        }
        finally
        {
            await _StopListeningAsync(listeningTask, cts);
        }
    }

    [Fact]
    public async Task should_throw_broker_connection_exception_for_bad_server_when_factory()
    {
        // given
        var badOptions = Options.Create(
            new NatsMessagingOptions
            {
                Servers = "nats://localhost:19999", // no server here
                ConfigureConnection = o => o with { ConnectTimeout = TimeSpan.FromSeconds(2) },
            }
        );
        var factory = new NatsConsumerClientFactory(badOptions, _serviceProvider);

        // when
        var act = async () => await factory.CreateAsync("test-group", 1);

        // then
        await act.Should().ThrowAsync<BrokerConnectionException>();
    }

    [Fact]
    public async Task should_pause_and_resume_consumer()
    {
        // given
        var streamName = $"pause-resume-{Guid.NewGuid():N}"[..28];
        var subject = $"{streamName}.test";
        await _EnsureStreamAsync(streamName, $"{streamName}.>");

        var options = _CreateOptions(enableStreamCreation: false);
        await using var client = new NatsConsumerClient("test-group", 0, options, _serviceProvider);
        await client.ConnectAsync(AbortToken);
        await client.FetchMessageNamesAsync([subject], AbortToken);
        await client.SubscribeAsync([subject], AbortToken);

        var messageReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var messageCount = 0;
        client.OnMessageCallback = (_, _) =>
        {
            Interlocked.Increment(ref messageCount);
            messageReceived.TrySetResult();
            return Task.CompletedTask;
        };
        client.OnLogCallback = _ => { };

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        // when — start listening, pause, publish, verify no delivery
        var listeningTask = client.ListeningAsync(TimeSpan.FromSeconds(1), cts.Token).AsTask();
        try
        {
            await Task.Delay(500, AbortToken); // let consumer start and create durable consumer

            await client.PauseAsync(AbortToken);
            await _PublishAsync(subject, "paused-msg"u8.ToArray());
            await Task.Delay(1000, AbortToken);

            var countWhilePaused = Volatile.Read(ref messageCount);

            // resume and wait for delivery via signal
            await client.ResumeAsync(AbortToken);
            await messageReceived.Task.WaitAsync(TimeSpan.FromSeconds(10), AbortToken);

            var countAfterResume = Volatile.Read(ref messageCount);

            // then
            countWhilePaused.Should().Be(0);
            countAfterResume.Should().BePositive();
        }
        finally
        {
            await _StopListeningAsync(listeningTask, cts);
        }
    }

    private IOptions<NatsMessagingOptions> _CreateOptions(bool enableStreamCreation)
    {
        return Options.Create(
            new NatsMessagingOptions
            {
                Servers = fixture.ConnectionString,
                EnableSubscriberClientStreamAndSubjectCreation = enableStreamCreation,
            }
        );
    }

    private async Task _EnsureStreamAsync(string streamName, string subjectPattern)
    {
        await fixture.EnsureStreamAsync(streamName, subjectPattern);
    }

    private async Task _PublishAsync(string subject, byte[] body)
    {
        var conn = await fixture.GetConnectionAsync();
        var js = new NatsJSContext(conn);
        await js.PublishAsync(
            subject,
            new ReadOnlyMemory<byte>(body),
            serializer: NatsRawSerializer<ReadOnlyMemory<byte>>.Default,
            cancellationToken: AbortToken
        );
    }

    private static async Task _StopListeningAsync(Task listeningTask, CancellationTokenSource cts)
    {
        await cts.CancelAsync();

        try
        {
            await listeningTask.WaitAsync(TimeSpan.FromSeconds(2));
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            // Normal shutdown.
        }
    }
}
