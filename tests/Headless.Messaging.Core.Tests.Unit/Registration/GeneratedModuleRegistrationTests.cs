// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Messaging.Registration;
using Headless.Messaging.Runtime;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Fixture = Headless.Messaging.GeneratedModuleFixture;

namespace Tests.Registration;

/// <summary>
/// Drives consumers compiled by the real source generator: a module adds the generated <c>MessagingModule</c>, its
/// consumers register with the identity, lane, and data their attributes declare, and a delivery runs the generated
/// dispatcher through the consume pipeline, which builds the consumer from the delivery's scope.
/// </summary>
public sealed class GeneratedModuleRegistrationTests : TestBase
{
    [Fact]
    public void should_register_each_generated_consumer_with_its_declared_identity_lane_and_data()
    {
        // given
        using var provider = _BuildProvider(services =>
            services.AddHeadlessMessaging(setup => setup.AddModule<Fixture.MessagingModule>())
        );

        // when
        var consumers = _Consumers(provider);

        // then
        consumers.Should().HaveCount(4);
        var projection = consumers.Single(x => x.ConsumerType == typeof(Fixture.InvoiceProjection));
        projection.MessageType.Should().Be<Fixture.InvoiceIssued>();
        projection.Lane.Should().Be(MessageLane.Bus);
        projection.ConsumerIdentity.Should().Be(Fixture.InvoiceProjection.Identity);
        projection.EveryInstance.Should().BeTrue();
        projection.DeclaringModule.Should().Be(typeof(Fixture.MessagingModule).FullName);

        var issue = consumers.Single(x => x.ConsumerType == typeof(Fixture.IssueInvoice));
        issue.MessageType.Should().Be<Fixture.IssueInvoiceCommand>();
        issue.Lane.Should().Be(MessageLane.Queue);
        issue.ConsumerIdentity.Should().Be(Fixture.IssueInvoice.Identity);
        issue.EveryInstance.Should().BeFalse();

        consumers
            .Where(x => x.ConsumerType == typeof(Fixture.LedgerProjection))
            .Select(x => (x.MessageType, x.ConsumerIdentity))
            .Should()
            .BeEquivalentTo([
                (typeof(Fixture.LedgerEntryPosted), Fixture.LedgerProjection.Identity),
                (typeof(Fixture.LedgerEntryReversed), Fixture.LedgerProjection.Identity),
            ]);
    }

    [Fact]
    public void should_register_a_module_once_when_contributions_and_the_host_add_it_in_any_order()
    {
        // given
        using var provider = _BuildProvider(services =>
        {
            services.ConfigureMessaging(messaging => messaging.AddModule<Fixture.MessagingModule>());
            services.AddHeadlessMessaging(setup => setup.AddModule<Fixture.MessagingModule>());
            services.ConfigureMessaging(messaging =>
                messaging.AddModule<Fixture.MessagingModule>().AddModule<Fixture.MessagingModule>()
            );
        });

        // when
        var consumers = _Consumers(provider);

        // then
        consumers
            .Select(x => x.ConsumerIdentity)
            .Should()
            .BeEquivalentTo(
                Fixture.InvoiceProjection.Identity,
                Fixture.IssueInvoice.Identity,
                Fixture.LedgerProjection.Identity,
                Fixture.LedgerProjection.Identity
            );
    }

    [Fact]
    public void should_register_no_generated_consumer_when_no_module_is_added()
    {
        // given
        using var provider = _BuildProvider(services => services.AddHeadlessMessaging(_ => { }));

        // when
        var consumers = _Consumers(provider);

        // then
        consumers.Should().BeEmpty();
    }

    [Fact]
    public void should_take_the_name_and_contract_version_the_message_contract_declares()
    {
        // given
        using var provider = _BuildProvider(services =>
        {
            services.ConfigureMessaging(messaging =>
            {
                messaging.Message<Fixture.InvoiceIssued>("fixture.invoice-issued", "2");
                messaging.AddModule<Fixture.MessagingModule>();
            });
            services.AddHeadlessMessaging(_ => { });
        });

        // when
        var projection = _Consumers(provider).Single(x => x.ConsumerType == typeof(Fixture.InvoiceProjection));

        // then
        projection.MessageName.Should().Be("fixture.invoice-issued");
        projection.MessageContractVersion.Should().Be("2");
    }

    [Fact]
    public async Task should_dispatch_a_bus_delivery_to_the_generated_consumer_built_from_the_delivery_scope()
    {
        // given
        await using var provider = _BuildProvider(services =>
            services.AddHeadlessMessaging(setup => setup.AddModule<Fixture.MessagingModule>())
        );
        var descriptor = _Descriptor(provider, Fixture.InvoiceProjection.Identity);

        // when
        await provider
            .GetRequiredService<ISubscribeInvoker>()
            .InvokeAsync(_Delivery(descriptor, new Fixture.InvoiceIssued("INV-7")), AbortToken);

        // then
        provider
            .GetRequiredService<Fixture.FixtureProbe>()
            .Calls.Should()
            .Equal("projected INV-7 on Bus", "projection disposed");
    }

    [Fact]
    public async Task should_run_lifecycle_hooks_around_a_queue_delivery_to_the_generated_consumer()
    {
        // given
        await using var provider = _BuildProvider(services =>
            services.AddHeadlessMessaging(setup => setup.AddModule<Fixture.MessagingModule>())
        );
        var descriptor = _Descriptor(provider, Fixture.IssueInvoice.Identity);

        // when
        await provider
            .GetRequiredService<ISubscribeInvoker>()
            .InvokeAsync(_Delivery(descriptor, new Fixture.IssueInvoiceCommand("ORD-3")), AbortToken);

        // then
        provider
            .GetRequiredService<Fixture.FixtureProbe>()
            .Calls.Should()
            .Equal("issue starting", "issued ORD-3 on Queue", "issue stopping");
    }

    private static ServiceProvider _BuildProvider(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<Fixture.FixtureProbe>();
        configure(services);

        return services.BuildServiceProvider();
    }

    // Selecting candidates drains the recorded registrations into the registry, as the bootstrapper does at startup.
    private static IReadOnlyList<ConsumerMetadata> _Consumers(IServiceProvider provider)
    {
        provider.GetRequiredService<IConsumerServiceSelector>().SelectCandidates();
        return provider.GetRequiredService<ConsumerRegistry>().GetAll();
    }

    private static ConsumerExecutorDescriptor _Descriptor(IServiceProvider provider, string identity) =>
        provider
            .GetRequiredService<IConsumerServiceSelector>()
            .SelectCandidates()
            .Single(x => string.Equals(x.ConsumerIdentity, identity, StringComparison.Ordinal));

    private static ConsumerContext _Delivery(ConsumerExecutorDescriptor descriptor, object message)
    {
        var origin = new Message(
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [Headers.MessageId] = "msg-1",
                [Headers.MessageName] = descriptor.MessageName,
            },
            message
        );

        return new ConsumerContext(
            descriptor,
            new MediumMessage
            {
                StorageId = Guid.NewGuid(),
                Origin = origin,
                Content = "{}",
                Lane = descriptor.Lane,
                Added = DateTimeOffset.UtcNow,
            }
        );
    }
}
