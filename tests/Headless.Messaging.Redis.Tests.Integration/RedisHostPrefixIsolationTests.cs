// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;
using Headless.Messaging;
using Headless.Messaging.Redis;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using MessagingHeaders = Headless.Messaging.Headers;

namespace Tests;

/// <summary>
/// Hosts that set different <c>Headless:Prefix</c> values share one Redis server without receiving each other's
/// messages, even with the same message name and consumer identity; hosts with the same prefix still share stream keys.
/// </summary>
[Collection<RedisMessagingFixture>]
public sealed class RedisHostPrefixIsolationTests(RedisMessagingFixture fixture) : TestBase
{
    private static readonly TimeSpan _Receive = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan _Quiet = TimeSpan.FromSeconds(2);

    [Theory]
    [InlineData(MessageLane.Bus)]
    [InlineData(MessageLane.Queue)]
    public async Task should_not_deliver_across_hosts_with_different_prefixes(MessageLane lane)
    {
        // given - three environments on one Redis server, one of them unprefixed, using the same name and identity
        var name = _UniqueName();
        var group = lane == MessageLane.Bus ? "orders.projection" : name;
        await using var first = await _HostAsync("wta", lane, name, group);
        await using var second = await _HostAsync("wtb", lane, name, group);
        await using var unprefixed = await _HostAsync("", lane, name, group);

        foreach (
            var (sender, others) in new[]
            {
                (first, new[] { second, unprefixed }),
                (second, new[] { first, unprefixed }),
                (unprefixed, new[] { first, second }),
            }
        )
        {
            // when
            var messageId = Guid.NewGuid().ToString("N");
            var result = await sender.PublishAsync(_Message(name, messageId, lane), AbortToken);

            // then
            result.Succeeded.Should().BeTrue(result.Exception?.Message);
            var delivery = await sender.ReceiveAsync(_Receive, AbortToken);
            delivery.Message.Id.Should().Be(messageId);
            await sender.Consumer.CommitAsync(delivery.SettlementValue, AbortToken);
            foreach (var other in others)
            {
                (await other.RemainsEmptyAsync(_Quiet, AbortToken))
                    .Should()
                    .BeTrue("a host with another prefix must not see this host's messages");
            }
        }
    }

    [Fact]
    public async Task should_fan_out_across_hosts_with_the_same_prefix()
    {
        // given - two hosts in one environment, each consuming the message under its own identity
        var name = _UniqueName();
        await using var publisher = await _HostAsync("wta", MessageLane.Bus, name, "orders.projection");
        await using var peer = await _HostAsync("wta", MessageLane.Bus, name, "orders.audit");

        // when
        var messageId = Guid.NewGuid().ToString("N");
        var result = await publisher.PublishAsync(_Message(name, messageId, MessageLane.Bus), AbortToken);

        // then - both identities read the one prefixed stream key, as unprefixed hosts always did
        result.Succeeded.Should().BeTrue(result.Exception?.Message);
        (await publisher.ReceiveAsync(_Receive, AbortToken)).Message.Id.Should().Be(messageId);
        (await peer.ReceiveAsync(_Receive, AbortToken)).Message.Id.Should().Be(messageId);
    }

    [Fact]
    public async Task should_deliver_a_queue_message_to_a_consumer_on_another_host_with_the_same_prefix()
    {
        // given - one host only publishes, another host in the same environment consumes the queue
        var name = _UniqueName();
        await using var consumer = await _HostAsync("wta", MessageLane.Queue, name, name);
        await using var publisherHost = _Provider("wta");
        var producer = publisherHost.GetRequiredService<IQueueTransport>();

        // when
        var messageId = Guid.NewGuid().ToString("N");
        var result = await producer.SendAsync(_Message(name, messageId, MessageLane.Queue), AbortToken);

        // then
        result.Succeeded.Should().BeTrue(result.Exception?.Message);
        (await consumer.ReceiveAsync(_Receive, AbortToken)).Message.Id.Should().Be(messageId);
    }

    private static string _UniqueName() => $"iso{Guid.NewGuid():N}"[..16] + ".created";

    private ServiceProvider _Provider(string prefix)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        if (prefix.Length > 0)
        {
            services.ConfigureHeadlessPrefix(prefix);
        }

        services.AddHeadlessMessaging(setup =>
            setup.UseRedis(options => options.Configuration = ConfigurationOptions.Parse(fixture.ConnectionString))
        );
        return services.BuildServiceProvider();
    }

    private async Task<TransportConsumerConformanceSession> _HostAsync(
        string prefix,
        MessageLane lane,
        string name,
        string group
    )
    {
        var provider = _Provider(prefix);
        TransportConsumerConformanceSession? session = null;
        try
        {
            var producer =
                lane == MessageLane.Bus
                    ? (ITransport)provider.GetRequiredService<IBusTransport>()
                    : provider.GetRequiredService<IQueueTransport>();
            var consumer = await provider
                .GetRequiredService<IConsumerClientFactory>()
                .CreateAsync(new ConsumerClientRequest(group, 1, lane) { ProvisionsTopology = true }, AbortToken);
            await consumer.SubscribeAsync(await consumer.FetchMessageNamesAsync([name], AbortToken), AbortToken);
            session = new TransportConsumerConformanceSession(
                name,
                producer,
                consumer,
                _Quiet,
                async () => await provider.DisposeAsync()
            );
            await session.StartAsync(cancellationToken: AbortToken);
            return session;
        }
        catch
        {
            if (session is not null)
            {
                await session.DisposeAsync();
            }
            else
            {
                await provider.DisposeAsync();
            }

            throw;
        }
    }

    private static TransportMessage _Message(string name, string messageId, MessageLane lane)
    {
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [MessagingHeaders.MessageId] = messageId,
            [MessagingHeaders.MessageName] = name,
            [MessagingHeaders.Intent] = lane.ToString(),
        };
        return new TransportMessage(headers, "{}"u8.ToArray());
    }
}
