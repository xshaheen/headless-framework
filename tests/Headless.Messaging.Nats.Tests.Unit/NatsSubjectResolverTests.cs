// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel;
using Headless.Hosting;
using Headless.Messaging;
using Headless.Messaging.Nats;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using MessagingHeaders = Headless.Messaging.Headers;

namespace Tests;

public sealed class NatsSubjectResolverTests : TestBase
{
    private const string _Name = "orders.placed";

    [Theory]
    [InlineData("", MessageLane.Bus)]
    [InlineData("", MessageLane.Queue)]
    [InlineData("wt1", MessageLane.Bus)]
    [InlineData("wt1", MessageLane.Queue)]
    public async Task should_return_the_subject_the_transport_publishes_to(string prefix, MessageLane lane)
    {
        // given
        await using var provider = _Provider(prefix);
        var resolver = provider.GetRequiredService<NatsSubjectResolver>();

        // when
        var subject = resolver.Subject(lane, _Name);

        // then
        subject.Should().Be(NatsTransport.ResolveSubject(_Message(_Name), lane, hostPrefix: prefix));
    }

    [Theory]
    [InlineData("", MessageLane.Bus)]
    [InlineData("", MessageLane.Queue)]
    [InlineData("wt1", MessageLane.Bus)]
    [InlineData("wt1", MessageLane.Queue)]
    public async Task should_return_the_shard_subject_the_transport_publishes_to(string prefix, MessageLane lane)
    {
        // given
        await using var provider = _Provider(prefix);
        var resolver = provider.GetRequiredService<NatsSubjectResolver>();

        // when
        var subject = resolver.Subject(lane, _Name, "tenant-7");

        // then
        subject
            .Should()
            .Be(NatsTransport.ResolveSubject(_Message(_Name, shard: "tenant-7"), lane, hostPrefix: prefix));
    }

    [Theory]
    [InlineData("", MessageLane.Bus)]
    [InlineData("wt1", MessageLane.Queue)]
    public async Task should_resolve_a_declared_contract_by_type_with_the_message_name_prefix(
        string prefix,
        MessageLane lane
    )
    {
        // given
        await using var provider = _Provider(prefix, messageNamePrefix: "app");
        var resolver = provider.GetRequiredService<NatsSubjectResolver>();

        // when
        var subject = resolver.Subject<OrderPlaced>(lane);

        // then
        subject.Should().Be(NatsTransport.ResolveSubject(_Message($"app.{_Name}"), lane, hostPrefix: prefix));
    }

    [Theory]
    [InlineData("", "headless", "headless.bus", "headless.queue")]
    [InlineData("wt1", "wt1.headless", "wt1.headless.bus", "wt1.headless.queue")]
    public async Task should_return_the_subject_root_and_lane_roots(
        string prefix,
        string root,
        string busRoot,
        string queueRoot
    )
    {
        // given
        await using var provider = _Provider(prefix);
        var resolver = provider.GetRequiredService<NatsSubjectResolver>();

        // then - every derived subject starts with its lane root
        resolver.Root.Should().Be(root);
        resolver.LaneRoot(MessageLane.Bus).Should().Be(busRoot);
        resolver.LaneRoot(MessageLane.Queue).Should().Be(queueRoot);
        resolver.Subject(MessageLane.Bus, _Name).Should().StartWith(busRoot + ".");
        resolver.Subject(MessageLane.Queue, _Name).Should().StartWith(queueRoot + ".");
    }

    [Fact]
    public async Task should_throw_when_the_contract_type_is_not_declared()
    {
        // given
        await using var provider = _Provider("");
        var resolver = provider.GetRequiredService<NatsSubjectResolver>();

        // when
        var act = () => resolver.Subject<UndeclaredMessage>(MessageLane.Bus);

        // then
        act.Should().Throw<InvalidOperationException>().WithMessage("*not declared on the Bus lane*");
    }

    [Fact]
    public async Task should_throw_for_a_message_bound_to_an_external_subject()
    {
        // given
        await using var provider = _Provider(
            "",
            configureNats: options => options.Streams.Own("ACME", stream => stream.Subjects("acme.>")),
            configureContract: contract =>
                contract.OnQueue(queue => queue.UseNats(nats => nats.ExternalSubject("acme.runs.result")))
        );
        var resolver = provider.GetRequiredService<NatsSubjectResolver>();

        // when
        var act = () => resolver.Subject(MessageLane.Queue, _Name);

        // then
        act.Should().Throw<InvalidOperationException>().WithMessage("*external NATS subject 'acme.runs.result'*");
    }

    [Fact]
    public async Task should_reject_an_invalid_shard_and_lane()
    {
        // given
        await using var provider = _Provider("");
        var resolver = provider.GetRequiredService<NatsSubjectResolver>();

        // when
        var shard = () => resolver.Subject(MessageLane.Bus, _Name, "a.b");
        var lane = () => resolver.LaneRoot((MessageLane)99);
        var name = () => resolver.Subject(MessageLane.Bus, " ");

        // then
        shard.Should().Throw<ArgumentException>().WithParameterName("shard");
        lane.Should().Throw<InvalidEnumArgumentException>();
        name.Should().Throw<ArgumentException>();
    }

    private static ServiceProvider _Provider(
        string prefix,
        string? messageNamePrefix = null,
        Action<NatsMessagingOptions>? configureNats = null,
        Action<IMessageContractBuilder<OrderPlaced>>? configureContract = null
    )
    {
        var services = new ServiceCollection();
        services.AddLogging();

        if (prefix.Length > 0)
        {
            services.ConfigureHeadlessPrefix(prefix);
        }

        services.ConfigureMessaging(messaging =>
        {
            var contract = messaging.Message<OrderPlaced>(_Name);
            configureContract?.Invoke(contract);
        });
        services.AddHeadlessMessaging(setup =>
        {
            setup.Options.MessageNamePrefix = messageNamePrefix;
            setup.UseNats(options =>
            {
                options.Servers = "nats://localhost:4222";
                configureNats?.Invoke(options);
            });
        });

        return services.BuildServiceProvider();
    }

    private static TransportMessage _Message(string name, string? shard = null)
    {
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            { MessagingHeaders.MessageId, "msg-1" },
            { MessagingHeaders.MessageName, name },
        };

        if (shard is not null)
        {
            headers[NatsMessagingHeaders.SubjectShard] = shard;
        }

        return new TransportMessage(headers, "body"u8.ToArray());
    }

    public sealed record OrderPlaced(string CustomerId);

    public sealed record UndeclaredMessage;
}
