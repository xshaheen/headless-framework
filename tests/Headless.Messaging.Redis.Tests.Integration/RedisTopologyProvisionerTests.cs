// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;
using Headless.Messaging;
using Headless.Messaging.Persistence;
using Headless.Messaging.Redis;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using Tests.Helpers;

namespace Tests;

/// <summary>
/// The Redis Streams consumer groups as a deploy-time resource: setup creates the streams and groups the registered
/// consumers and Queue messages need, the host only reads them, clear resets them, and teardown deletes them.
/// </summary>
[Collection<RedisMessagingFixture>]
public sealed class RedisTopologyProvisionerTests(RedisMessagingFixture fixture) : TestBase
{
    private const string _Body = """{"Value":"first"}""";

    [Fact]
    public async Task should_set_up_check_clear_and_tear_down_the_shared_topology()
    {
        // given
        var (provider, name) = _Build();
        await using var _ = provider;
        var stream = RedisPhysicalAddress.QueueStream("", name);
        var database = await _DatabaseAsync();
        var topology = (await _ResourcesAsync(provider)).Should().ContainSingle(r => r.Kind == "RedisStreams").Which;
        topology.Name.Should().Be("topology");
        await FluentActions
            .Awaiting(() => topology.VerifyAsync(AbortToken))
            .Should()
            .ThrowAsync<ResourceProvisioningException>()
            .WithMessage($"*'{name}' on stream '{stream}' does not exist. Run 'resources provision'*");

        // when
        await provider.ProvisionResourcesAsync(AbortToken);

        // then
        (await database.StreamGroupInfoAsync(stream))
            .Should()
            .ContainSingle(g => g.Name == name);
        await topology.VerifyAsync(AbortToken);

        // and when - an entry waits in the stream, then the state is cleared
        await database.StreamAddAsync(stream, "body", _Body);
        await provider.PurgeResourcesAsync(AbortToken);

        // then - the entry is gone and the group stays, with nothing left to deliver
        (await database.StreamLengthAsync(stream))
            .Should()
            .Be(0);
        (await database.StreamGroupInfoAsync(stream)).Should().ContainSingle(g => g.Name == name);
        (await database.StreamReadGroupAsync(stream, name, "probe", StreamPosition.NewMessages)).Should().BeEmpty();

        // and when
        await provider.DeleteResourcesAsync(AbortToken);

        // then
        (await database.KeyExistsAsync(stream))
            .Should()
            .BeFalse("no group reads the stream any more");
        await FluentActions
            .Awaiting(() => topology.VerifyAsync(AbortToken))
            .Should()
            .ThrowAsync<ResourceProvisioningException>()
            .WithMessage("*does not exist. Run 'resources provision'*");
    }

    [Fact]
    public async Task should_fail_bootstrap_naming_resources_provision_and_create_nothing_when_the_topology_is_missing()
    {
        // given
        var (provider, name) = _Build();
        await using var _ = provider;
        var stream = RedisPhysicalAddress.QueueStream("", name);

        // when
        var act = () => provider.GetRequiredService<IBootstrapper>().BootstrapAsync(AbortToken);

        // then - a reachable Redis without the group is a deployment fault, not an outage to retry through
        await act.Should().ThrowAsync<ResourceProvisioningException>().WithMessage("*Run 'resources provision'*");
        (await (await _DatabaseAsync()).KeyExistsAsync(stream))
            .Should()
            .BeFalse("the running host creates no stream or group");
    }

    [Fact]
    public async Task should_hold_the_resources_warmup_naming_the_missing_group_until_resources_provision_runs()
    {
        // given
        var (provider, name) = _Build();
        await using var _ = provider;
        var stream = RedisPhysicalAddress.QueueStream("", name);
        var warmup = provider.GetServices<IWarmup>().Should().ContainSingle(w => w.Name == "Resources").Which;

        // when
        var beforeSetup = () => warmup.WarmUpAsync(AbortToken);

        // then
        await beforeSetup
            .Should()
            .ThrowAsync<ResourceProvisioningException>()
            .WithMessage($"*Redis Streams consumer group '{name}' on stream '{stream}' does not exist*");
        (await (await _DatabaseAsync()).KeyExistsAsync(stream)).Should().BeFalse("the check only reads");

        // and when - the deployment creates the topology
        await provider.ProvisionResourcesAsync(AbortToken);

        // then
        await warmup.WarmUpAsync(AbortToken);
    }

    [Fact]
    public async Task should_consume_once_the_deployment_set_the_topology_up()
    {
        // given
        var (provider, name) = _Build();
        await using var _ = provider;
        await provider.ProvisionResourcesAsync(AbortToken);

        // when
        await provider.GetRequiredService<IBootstrapper>().BootstrapAsync(AbortToken);
        await (await _DatabaseAsync()).StreamAddAsync(RedisPhysicalAddress.QueueStream("", name), "body", _Body);

        // then
        await provider
            .GetRequiredService<OrderConsumer>()
            .Received.Task.WaitAsync(TimeSpan.FromSeconds(30), AbortToken);
    }

    [Fact]
    public async Task should_keep_another_identitys_group_and_entries_on_a_shared_bus_stream()
    {
        // given - two services consume the same Bus message, each through its own group on one stream
        var name = $"topology-{Guid.NewGuid():N}";
        var (billing, _) = _Build(name, MessageLane.Bus, consumer: _BillingIdentity);
        await using var _ = billing;
        var (shipping, _) = _Build(name, MessageLane.Bus, consumer: _ShippingIdentity);
        await using var __ = shipping;
        var stream = RedisPhysicalAddress.BusStream("", name);
        var billingGroup = RedisPhysicalAddress.ConsumerGroup(MessageLane.Bus, _BillingIdentity);
        var shippingGroup = RedisPhysicalAddress.ConsumerGroup(MessageLane.Bus, _ShippingIdentity);
        var database = await _DatabaseAsync();
        await billing.ProvisionResourcesAsync(AbortToken);
        await shipping.ProvisionResourcesAsync(AbortToken);
        await database.StreamAddAsync(stream, "body", _Body);

        // when
        await billing.PurgeResourcesAsync(AbortToken);

        // then - billing skips its backlog; the stream keeps the entry shipping has yet to read
        (await database.StreamLengthAsync(stream))
            .Should()
            .Be(1);
        (await database.StreamReadGroupAsync(stream, billingGroup, "probe", StreamPosition.NewMessages))
            .Should()
            .BeEmpty();
        (await database.StreamReadGroupAsync(stream, shippingGroup, "probe", StreamPosition.NewMessages))
            .Should()
            .ContainSingle();

        // and when
        await billing.DeleteResourcesAsync(AbortToken);

        // then - billing's group goes; the stream and shipping's group stay
        (await database.StreamGroupInfoAsync(stream))
            .Select(g => g.Name)
            .Should()
            .Equal(shippingGroup);
        await (await _ResourcesAsync(shipping))
            .Single(r => string.Equals(r.Kind, "RedisStreams", StringComparison.Ordinal))
            .VerifyAsync(AbortToken);

        // and when - the last service tears down
        await shipping.DeleteResourcesAsync(AbortToken);

        // then
        (await database.KeyExistsAsync(stream))
            .Should()
            .BeFalse();
    }

    [Fact]
    public async Task should_leave_a_group_this_host_only_publishes_to_untouched_by_clear_and_teardown()
    {
        // given - the consumer service owns the group; the publisher only sends to it
        var (consumer, name) = _Build();
        await using var _ = consumer;
        var (publisher, _) = _Build(name, consumer: null);
        await using var __ = publisher;
        var stream = RedisPhysicalAddress.QueueStream("", name);
        var database = await _DatabaseAsync();
        await consumer.ProvisionResourcesAsync(AbortToken);
        await publisher.ProvisionResourcesAsync(AbortToken);
        await database.StreamAddAsync(stream, "body", _Body);

        // when
        await publisher.PurgeResourcesAsync(AbortToken);
        await publisher.DeleteResourcesAsync(AbortToken);

        // then
        (await database.StreamLengthAsync(stream))
            .Should()
            .Be(1, "the publisher neither purged nor deleted the consumer's group");
        (await database.StreamGroupInfoAsync(stream)).Should().ContainSingle(g => g.Name == name);
    }

    [Fact]
    public async Task should_create_a_publisher_only_queue_group_so_entries_wait_for_the_consumer()
    {
        // given
        var (publisher, name) = _Build(consumer: null);
        await using var _ = publisher;

        // when
        await publisher.ProvisionResourcesAsync(AbortToken);

        // then
        (await (await _DatabaseAsync()).StreamGroupInfoAsync(RedisPhysicalAddress.QueueStream("", name)))
            .Should()
            .ContainSingle(g => g.Name == name);
    }

    [Fact]
    public async Task should_provision_the_streams_under_the_host_prefix()
    {
        // given
        var prefix = $"wt{Guid.NewGuid():N}"[..12];
        var (provider, name) = _Build(prefix: prefix);
        await using var _ = provider;
        var database = await _DatabaseAsync();

        // when
        await provider.ProvisionResourcesAsync(AbortToken);

        // then
        (await database.KeyExistsAsync(RedisPhysicalAddress.QueueStream(prefix, name)))
            .Should()
            .BeTrue();
        (await database.KeyExistsAsync(RedisPhysicalAddress.QueueStream("", name))).Should().BeFalse();
        await provider.DeleteResourcesAsync(AbortToken);
    }

    private const string _QueueIdentity = "tests.redis.topology";
    private const string _BillingIdentity = "tests.redis.topology.billing";
    private const string _ShippingIdentity = "tests.redis.topology.shipping";

    private (ServiceProvider Provider, string Name) _Build(
        string? name = null,
        MessageLane lane = MessageLane.Queue,
        string? consumer = _QueueIdentity,
        string prefix = ""
    )
    {
        name ??= $"topology-{Guid.NewGuid():N}";
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<OrderConsumer>();
        if (prefix.Length > 0)
        {
            services.ConfigureHeadlessPrefix(prefix);
        }

        services.AddHeadlessMessaging(setup =>
        {
            setup.UseRedis(fixture.ConnectionString);
            setup.UseInMemoryStorage();
            setup.Options.MinimumInboxGuarantee = InboxGuarantee.ProcessLocal;
        });
        services.ConfigureMessaging(messaging =>
        {
            if (lane is MessageLane.Queue)
            {
                messaging.Message<Order>(name).OnQueue(queue => queue.RawPayload());
            }
            else
            {
                messaging.Message<Order>(name).OnBus(bus => bus.RawPayload());
            }

            switch (consumer)
            {
                case _QueueIdentity:
                    messaging.AddModule<OrderModule>();
                    break;
                case _BillingIdentity:
                    messaging.AddModule<BillingModule>();
                    break;
                case _ShippingIdentity:
                    messaging.AddModule<ShippingModule>();
                    break;
            }
        });

        return (services.BuildServiceProvider(), name);
    }

    private async Task<IDatabase> _DatabaseAsync() => (await fixture.GetAdminConnectionAsync()).GetDatabase();

    private static async Task<List<IResourceProvisioner>> _ResourcesAsync(IServiceProvider provider)
    {
        var resources = new List<IResourceProvisioner>();

        foreach (var source in provider.GetServices<IResourceProvisionerSource>())
        {
            resources.AddRange(await source.GetProvisionersAsync(AbortToken));
        }

        return resources;
    }

    public sealed record Order(string Value);

    public sealed class OrderModule : IMessagingModule
    {
        public static void Register(MessagingCatalogBuilder catalog) =>
            catalog.AddQueueConsumer<OrderConsumer, Order>(
                _QueueIdentity,
                TestConsumerDispatch.FromServices<OrderConsumer, Order>()
            );
    }

    public sealed class BillingModule : IMessagingModule
    {
        public static void Register(MessagingCatalogBuilder catalog) =>
            catalog.AddBusConsumer<OrderConsumer, Order>(
                _BillingIdentity,
                everyInstance: false,
                TestConsumerDispatch.FromServices<OrderConsumer, Order>()
            );
    }

    public sealed class ShippingModule : IMessagingModule
    {
        public static void Register(MessagingCatalogBuilder catalog) =>
            catalog.AddBusConsumer<OrderConsumer, Order>(
                _ShippingIdentity,
                everyInstance: false,
                TestConsumerDispatch.FromServices<OrderConsumer, Order>()
            );
    }

    public sealed class OrderConsumer : IConsume<Order>
    {
        public TaskCompletionSource Received { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask ConsumeAsync(ConsumeContext<Order> context, CancellationToken cancellationToken)
        {
            Received.TrySetResult();

            return ValueTask.CompletedTask;
        }
    }
}
