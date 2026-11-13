// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Text;
using Headless.Messaging;
using Headless.Messaging.Nats;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using MessagingHeaders = Headless.Messaging.Headers;

namespace Tests;

/// <summary>
/// A Bus consumer of several message types on one stream receives them in the order the stream stored them, and its
/// first start after an upgrade takes over from the per-message durables earlier releases created.
/// </summary>
[Collection("Nats")]
public sealed class NatsCrossTypeOrderingTests(NatsFixture fixture) : TestBase
{
    private const int _MessagesPerRound = 50;
    private const int _Rounds = 3;

    private readonly IServiceProvider _serviceProvider = new ServiceCollection().BuildServiceProvider();

    [Fact]
    public async Task should_consume_two_message_types_in_the_order_the_stream_stored_them()
    {
        // given - one sequential consumer of "registered" and "updated" events about the same entities
        var key = $"order{Guid.NewGuid():N}"[..20];
        string[] names = [$"{key}.registered", $"{key}.updated"];
        await using var listener = await _ListenAsync(_MemoryOptions(), "ordering.projection", names);

        for (var round = 0; round < _Rounds; round++)
        {
            // when - the two types alternate, as a registration and its update would
            var expected = new List<string>();
            for (var i = 0; i < _MessagesPerRound; i++)
            {
                var body = $"{round}:{i}";
                expected.Add(body);
                await _PublishAsync(names[i % 2], body);
            }

            // then
            var received = await listener.TakeAsync(_MessagesPerRound);
            received.Should().Equal(expected, $"round {round} must arrive in stream order across both message types");
        }
    }

    [Fact]
    public async Task should_deliver_what_the_per_message_durables_left_pending_and_remove_them()
    {
        // given - an earlier release consumed each message through its own durable; "updated" is caught up and
        // "registered" still holds three messages a stopped host never received
        var key = $"handoff{Guid.NewGuid():N}"[..20];
        string[] names = [$"{key}.registered", $"{key}.updated"];
        const string identity = "handoff.projection";
        var options = _MemoryOptions();
        var stream = await _ProvisionAsync(options, names);
        var js = new NatsJSContext(await fixture.GetConnectionAsync());
        foreach (var name in names)
        {
            await _CreateLegacyDurableAsync(js, stream, identity, name);
        }

        await _PublishAsync(names[1], "updated-before");
        await _DrainLegacyAsync(js, stream, NatsPhysicalAddress.LegacyBusDurable(identity, names[1]), 1);
        await _PublishAsync(names[0], "pending-1");
        await _PublishAsync(names[1], "pending-2");
        await _PublishAsync(names[0], "pending-3");

        // when
        await using (var listener = await _ListenAsync(options, identity, names))
        {
            // then - every message the legacy durables had not settled arrives once, in stream order
            (await listener.TakeAsync(3))
                .Should()
                .Equal("pending-1", "pending-2", "pending-3");
            await listener.ExpectNothingMoreAsync();
        }

        (await _ConsumerExistsAsync(js, stream, NatsPhysicalAddress.LegacyBusDurable(identity, names[0])))
            .Should()
            .BeFalse();
        (await _ConsumerExistsAsync(js, stream, NatsPhysicalAddress.LegacyBusDurable(identity, names[1])))
            .Should()
            .BeFalse();
        var durable = await js.GetConsumerAsync(stream, NatsPhysicalAddress.BusDurable(identity), AbortToken);
        durable.Info.Config.FilterSubjects.Should().BeEquivalentTo(names.Select(_Subject));

        // and a restart binds the handed-off durable again, keeping its start position, and goes on delivering
        await using var restarted = await _ListenAsync(options, identity, names);
        await _PublishAsync(names[1], "after-restart");
        (await restarted.TakeAsync(1)).Should().Equal("after-restart");
    }

    [Fact]
    public async Task should_not_replay_history_when_the_per_message_durables_were_caught_up()
    {
        // given - a limits-retained stream keeps what was consumed; the rarely published "registered" durable acknowledged
        // its last message long before the busy "updated" one, and both have nothing pending
        var key = $"caughtup{Guid.NewGuid():N}"[..20];
        string[] names = [$"{key}.registered", $"{key}.updated"];
        const string identity = "caughtup.projection";
        var streamName = $"CAUGHT_{key[8..]}";
        var natsOptions = new NatsMessagingOptions
        {
            Servers = fixture.ConnectionString,
            StreamProvisioning = NatsStreamProvisioning.Reconcile,
            StreamOptions = config => config.Storage = StreamConfigStorage.Memory,
        };
        natsOptions.Streams.Own(streamName, s => s.Subjects(NatsPhysicalAddress.Subject(MessageLane.Bus, $"{key}.>")));
        var options = Options.Create(natsOptions);
        var stream = await _ProvisionAsync(options, names);
        stream.Should().Be(streamName);
        var js = new NatsJSContext(await fixture.GetConnectionAsync());
        foreach (var name in names)
        {
            await _CreateLegacyDurableAsync(js, stream, identity, name);
        }

        await _PublishAsync(names[0], "old-registered");
        await _DrainLegacyAsync(js, stream, NatsPhysicalAddress.LegacyBusDurable(identity, names[0]), 1);
        for (var i = 0; i < 10; i++)
        {
            await _PublishAsync(names[1], $"old-updated-{i}");
        }

        await _DrainLegacyAsync(js, stream, NatsPhysicalAddress.LegacyBusDurable(identity, names[1]), 10);

        // when
        await using var listener = await _ListenAsync(options, identity, names);
        await _PublishAsync(names[0], "new");

        // then - only the message published after the switch
        (await listener.TakeAsync(1))
            .Should()
            .Equal("new");
        await listener.ExpectNothingMoreAsync();
    }

    private IOptions<NatsMessagingOptions> _MemoryOptions() =>
        Options.Create(
            new NatsMessagingOptions
            {
                Servers = fixture.ConnectionString,
                StreamProvisioning = NatsStreamProvisioning.Reconcile,
                StreamOptions = config => config.Storage = StreamConfigStorage.Memory,
            }
        );

    private async Task<string> _ProvisionAsync(IOptions<NatsMessagingOptions> options, string[] names)
    {
        await using var client = new NatsConsumerClient("provisioner", 0, options, _serviceProvider);
        await client.ConnectAsync(AbortToken);
        await client.FetchMessageNamesAsync(names, AbortToken);
        return new NatsStreamProvisioner(options).StreamName(MessageLane.Bus, names[0]);
    }

    // The durable an earlier release created for one message: per subject, starting at new messages.
    private static async Task _CreateLegacyDurableAsync(NatsJSContext js, string stream, string identity, string name)
    {
        await js.CreateOrUpdateConsumerAsync(
            stream,
            new ConsumerConfig(NatsPhysicalAddress.LegacyBusDurable(identity, name))
            {
                FilterSubject = _Subject(name),
                DeliverPolicy = ConsumerConfigDeliverPolicy.New,
                AckWait = TimeSpan.FromSeconds(30),
            },
            AbortToken
        );
    }

    private static async Task _DrainLegacyAsync(NatsJSContext js, string stream, string durable, int count)
    {
        var consumer = await js.GetConsumerAsync(stream, durable, AbortToken);
        var acked = 0;
        await foreach (
            var msg in consumer.FetchAsync(
                new NatsJSFetchOpts { MaxMsgs = count, Expires = TimeSpan.FromSeconds(5) },
                NatsRawSerializer<ReadOnlyMemory<byte>>.Default,
                AbortToken
            )
        )
        {
            await msg.AckAsync(new AckOpts { DoubleAck = true }, AbortToken);
            acked++;
        }

        acked.Should().Be(count);
    }

    private static async Task<bool> _ConsumerExistsAsync(NatsJSContext js, string stream, string durable)
    {
        try
        {
            await js.GetConsumerAsync(stream, durable, AbortToken);
            return true;
        }
        catch (NatsJSApiException e) when (e.Error.ErrCode == 10014)
        {
            return false;
        }
    }

    private async Task _PublishAsync(string name, string body)
    {
        var js = new NatsJSContext(await fixture.GetConnectionAsync());
        var ack = await js.PublishAsync(
            _Subject(name),
            new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(body)),
            serializer: NatsRawSerializer<ReadOnlyMemory<byte>>.Default,
            headers: new NatsHeaders
            {
                { MessagingHeaders.MessageId, Guid.NewGuid().ToString("N") },
                { MessagingHeaders.MessageName, name },
            },
            cancellationToken: AbortToken
        );
        ack.EnsureSuccess();
    }

    private static string _Subject(string name) => NatsPhysicalAddress.Subject(MessageLane.Bus, name);

    private async Task<Listener> _ListenAsync(IOptions<NatsMessagingOptions> options, string identity, string[] names)
    {
        var listener = new Listener(identity, options, _serviceProvider);
        try
        {
            await listener.StartAsync(names);
            return listener;
        }
        catch
        {
            await listener.DisposeAsync();
            throw;
        }
    }

    private sealed class Listener : IAsyncDisposable
    {
        private readonly NatsConsumerClient _client;
        private readonly CancellationTokenSource _stop = new();
        private readonly ConcurrentQueue<string> _received = new();
        private readonly ConcurrentQueue<string> _logs = new();
        private Task? _listening;

        public Listener(string identity, IOptions<NatsMessagingOptions> options, IServiceProvider serviceProvider)
        {
            var client = new NatsConsumerClient(identity, 0, options, serviceProvider);
            _client = client;
            client.OnMessageCallback = async (message, sender) =>
            {
                _received.Enqueue(Encoding.UTF8.GetString(message.Body.Span));
                await client.CommitAsync(sender, CancellationToken.None);
            };
            client.OnLogCallback = args => _logs.Enqueue($"{args.LogType}: {args.Reason}");
        }

        public async Task StartAsync(string[] names)
        {
            await _client.ConnectAsync(AbortToken);
            await _client.SubscribeAsync(await _client.FetchMessageNamesAsync(names, AbortToken), AbortToken);
            _listening = _client.ListeningAsync(TimeSpan.FromSeconds(1), _stop.Token).AsTask();
            await _client.WaitUntilReadyAsync(AbortToken).AsTask().WaitAsync(TimeSpan.FromSeconds(15), AbortToken);
        }

        public async Task<List<string>> TakeAsync(int count)
        {
            var taken = new List<string>(count);
            using var bound = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            while (taken.Count < count)
            {
                if (_received.TryDequeue(out var body))
                {
                    taken.Add(body);
                    continue;
                }

                if (bound.IsCancellationRequested)
                {
                    throw new TimeoutException(
                        $"Received {taken.Count} of {count} messages ({string.Join(", ", taken)}); logs: "
                            + string.Join(" | ", _logs)
                    );
                }

                await Task.Delay(10, AbortToken);
            }

            return taken;
        }

        // Long enough for a redelivery of anything already pulled, which the 30 s AckWait would not produce sooner, and for
        // a replayed backlog, which the pull would hand over at once.
        public async Task ExpectNothingMoreAsync()
        {
            await Task.Delay(TimeSpan.FromSeconds(1), AbortToken);
            _received.Should().BeEmpty();
        }

        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync();
            if (_listening is not null)
            {
#pragma warning disable ERP022 // The listening loop ends with the cancellation this test requested.
                try
                {
                    await _listening.WaitAsync(TimeSpan.FromSeconds(5));
                }
                catch
                {
                    // Shutdown only.
                }
#pragma warning restore ERP022
            }

            await _client.DisposeAsync();
            _stop.Dispose();
        }
    }
}
