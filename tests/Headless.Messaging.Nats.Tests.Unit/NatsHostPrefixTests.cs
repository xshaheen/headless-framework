// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;
using Headless.Messaging;
using Headless.Messaging.Nats;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class NatsHostPrefixTests : TestBase
{
    [Fact]
    public void should_keep_every_derived_name_unchanged_without_a_prefix()
    {
        // Hosts that set no prefix keep the names earlier releases created, byte for byte.
        NatsPhysicalAddress.Subject("", MessageLane.Bus, "orders.created").Should().Be("headless.bus.orders.created");
        NatsPhysicalAddress
            .Subject("", MessageLane.Queue, "orders.created")
            .Should()
            .Be("headless.queue.orders.created");
        NatsPhysicalAddress.Stream("", MessageLane.Bus, "orders").Should().Be("headless-bus-orders");
        NatsPhysicalAddress.BusDurable("", "billing").Should().Be("bus_billing");
        NatsPhysicalAddress.LegacyBusDurable("", "billing", "orders").Should().Be("bus-billing-orders");
        NatsPhysicalAddress.QueueDurable("", "orders").Should().Be("queue-orders");
        NatsPhysicalAddress.OwnedDeclaredStream("", "ORDERS").Should().Be("ORDERS");
        NatsPhysicalAddress.DeclaredSubject("", "headless.bus.orders.>").Should().Be("headless.bus.orders.>");
    }

    [Fact]
    public void should_put_the_prefix_in_front_of_every_derived_name()
    {
        NatsPhysicalAddress
            .Subject("wt1", MessageLane.Bus, "orders.created")
            .Should()
            .Be("wt1.headless.bus.orders.created");
        NatsPhysicalAddress.Stream("wt1", MessageLane.Queue, "orders").Should().Be("wt1_headless-queue-orders");
        NatsPhysicalAddress.BusDurable("wt1", "billing").Should().Be("wt1_bus_billing");
        NatsPhysicalAddress.LegacyBusDurable("wt1", "billing", "orders").Should().Be("wt1_bus-billing-orders");
        NatsPhysicalAddress.QueueDurable("wt1", "orders").Should().Be("wt1_queue-orders");
    }

    [Fact]
    public void should_keep_two_prefixes_from_deriving_one_name()
    {
        NatsPhysicalAddress
            .Subject("wt1", MessageLane.Bus, "orders")
            .Should()
            .NotBe(NatsPhysicalAddress.Subject("wt2", MessageLane.Bus, "orders"));
        NatsPhysicalAddress
            .Stream("wt1", MessageLane.Bus, "orders")
            .Should()
            .NotBe(NatsPhysicalAddress.Stream("wt2", MessageLane.Bus, "orders"));
        NatsPhysicalAddress.BusDurable("wt1", "billing").Should().NotBe(NatsPhysicalAddress.BusDurable("", "billing"));
    }

    [Fact]
    public void should_never_turn_a_prefixed_subject_into_a_reply_or_other_lane_subject()
    {
        // The prefix leads, so no prefix value can make a derived subject start with 'headless.'.
        var subject = NatsPhysicalAddress.Subject("reply", MessageLane.Bus, "orders");

        subject.Should().Be("reply.headless.bus.orders");
        subject.Should().NotStartWith("headless.");
    }

    [Fact]
    public void should_bound_a_prefixed_bus_durable_to_the_nats_limit()
    {
        var name = NatsPhysicalAddress.BusDurable(
            new string('p', HeadlessPrefixOptions.MaxLength),
            new string('a', 300)
        );

        name.Should().HaveLength(255);
        name.Should().StartWith(new string('p', HeadlessPrefixOptions.MaxLength) + "_bus_aaa");
    }

    [Fact]
    public void should_move_declared_streams_and_headless_subjects_under_the_prefix()
    {
        // given
        var catalog = new NatsStreamCatalog()
            .Own("ORDERS", stream => stream.Subjects("headless.queue.orders.>", "erp.orders.>"))
            .Bind("ERP", "erp.invoices.>", "headless.bus.invoices.>");

        // when
        catalog.ApplyHostPrefix("wt1");

        // then - an owned stream is this environment's copy; a bound stream keeps the name it was created under, and
        // a foreign subject keeps the subject its producer publishes on
        var owned = catalog.Streams[0];
        owned.Name.Should().Be("wt1_ORDERS");
        owned.Subjects.Should().Equal("wt1.headless.queue.orders.>", "erp.orders.>");
        var bound = catalog.Streams[1];
        bound.Name.Should().Be("ERP");
        bound.Subjects.Should().Equal("erp.invoices.>", "wt1.headless.bus.invoices.>");
    }

    [Fact]
    public async Task should_apply_the_host_prefix_and_state_it_at_startup()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.ConfigureHeadlessPrefix("wt1");

        // when
        services.AddHeadlessMessaging(setup => setup.UseNats("nats://localhost:4222"));
        await using var provider = services.BuildServiceProvider();

        // then - the provisioner derives prefixed names, and the host states the prefix when it starts
        provider.GetRequiredService<IOptions<NatsMessagingOptions>>().Value.HostPrefix.Should().Be("wt1");
        provider
            .GetRequiredService<NatsStreamProvisioner>()
            .StreamName(MessageLane.Bus, "orders.created")
            .Should()
            .Be("wt1_headless-bus-orders");
        services
            .Should()
            .Contain(descriptor =>
                descriptor.ServiceType == typeof(IHostedService)
                && descriptor.ImplementationType != null
                && descriptor.ImplementationType.Name == "HeadlessPrefixStartupLog"
            );
    }

    [Fact]
    public async Task should_leave_options_unprefixed_when_the_host_has_no_prefix()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();

        // when
        services.AddHeadlessMessaging(setup => setup.UseNats("nats://localhost:4222"));
        await using var provider = services.BuildServiceProvider();

        // then
        provider.GetRequiredService<IOptions<NatsMessagingOptions>>().Value.HostPrefix.Should().BeEmpty();
        provider
            .GetRequiredService<NatsStreamProvisioner>()
            .StreamName(MessageLane.Bus, "orders.created")
            .Should()
            .Be("headless-bus-orders");
    }
}
