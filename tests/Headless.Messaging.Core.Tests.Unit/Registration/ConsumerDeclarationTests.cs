// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Reliability;
using Headless.Testing.Tests;

namespace Tests.Registration;

public sealed class ConsumerDeclarationTests : TestBase
{
    [Fact]
    public void should_offer_every_instance_delivery_only_on_the_bus_lane()
    {
        _PublicPropertyNames(typeof(BusConsumerAttribute)).Should().Contain(nameof(BusConsumerAttribute.EveryInstance));
        _PublicPropertyNames(typeof(QueueConsumerAttribute))
            .Should()
            .NotContain(nameof(BusConsumerAttribute.EveryInstance));
    }

    private static IEnumerable<string> _PublicPropertyNames(Type type) =>
        type.GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public)
            .Select(static property => property.Name);

    [Fact]
    public void should_share_one_base_that_only_the_lane_attributes_can_derive_from()
    {
        typeof(BusConsumerAttribute).BaseType.Should().Be<MessageConsumerAttribute>();
        typeof(QueueConsumerAttribute).BaseType.Should().Be<MessageConsumerAttribute>();
        typeof(MessageConsumerAttribute)
            .GetConstructors(
                System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.NonPublic
            )
            .Should()
            .OnlyContain(constructor => constructor.IsFamilyAndAssembly);
    }

    [Fact]
    public void should_read_the_declared_identity_and_policy()
    {
        var attribute = (BusConsumerAttribute)
            Attribute.GetCustomAttribute(typeof(PriceCacheConsumer), typeof(MessageConsumerAttribute))!;

        attribute.Identity.Should().Be("billing.price-cache");
        attribute.EveryInstance.Should().BeTrue();
        attribute.Policy.Should().Be<TestFailurePolicy>();
    }

    [Fact]
    public void should_attribute_catalog_entries_to_the_module_that_added_them()
    {
        // given
        var catalog = new MessagingCatalogBuilder();

        // when
        catalog.AddModule(typeof(TestMessagingModule), static builder => TestMessagingModule.Register(builder));

        // then
        catalog
            .Consumers.Should()
            .BeEquivalentTo([
                new MessagingConsumerDeclaration(
                    typeof(TestMessagingModule).FullName!,
                    typeof(PriceCacheConsumer),
                    typeof(PriceChanged),
                    MessageLane.Bus,
                    "billing.price-cache",
                    EveryInstance: true,
                    typeof(TestFailurePolicy),
                    _NoDispatch
                ),
                new MessagingConsumerDeclaration(
                    typeof(TestMessagingModule).FullName!,
                    typeof(IssueInvoiceConsumer),
                    typeof(IssueInvoice),
                    MessageLane.Queue,
                    "billing.issue-invoice",
                    EveryInstance: false,
                    Policy: null,
                    _NoDispatch
                ),
            ]);
    }

    [Fact]
    public void should_reject_a_policy_type_that_is_not_a_failure_policy()
    {
        var catalog = new MessagingCatalogBuilder();

        var add = () =>
            catalog.AddQueueConsumer<IssueInvoiceConsumer, IssueInvoice>(
                "billing.issue-invoice",
                typeof(string),
                _NoDispatch
            );

        add.Should().Throw<ArgumentException>().WithMessage("*String*billing.issue-invoice*IFailurePolicy*");
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("")]
    public void should_reject_an_empty_identity(string identity)
    {
        var catalog = new MessagingCatalogBuilder();

        var add = () =>
            catalog.AddBusConsumer<PriceCacheConsumer, PriceChanged>(identity, everyInstance: false, null, _NoDispatch);

        add.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void should_reject_an_identity_longer_than_durable_storage_holds()
    {
        var catalog = new MessagingCatalogBuilder();
        var identity = "billing." + new string('x', ConsumerMetadata.ConsumerIdentityMaxLength);

        var add = () =>
            catalog.AddBusConsumer<PriceCacheConsumer, PriceChanged>(identity, everyInstance: false, null, _NoDispatch);

        add.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void should_reject_a_missing_dispatch()
    {
        var catalog = new MessagingCatalogBuilder();

        var add = () =>
            catalog.AddQueueConsumer<IssueInvoiceConsumer, IssueInvoice>("billing.issue-invoice", null, null!);

        add.Should().Throw<ArgumentNullException>();
    }

    // Stands in for a generated dispatcher; these tests only read the declarations.
    private static readonly MessageConsumerDispatch _NoDispatch = static (_, _, _) => ValueTask.CompletedTask;

    public sealed record PriceChanged(string Sku);

    public sealed record IssueInvoice(string OrderId);

    public sealed class TestFailurePolicy : IFailurePolicy;

    [BusConsumer("billing.price-cache", EveryInstance = true, Policy = typeof(TestFailurePolicy))]
    public sealed class PriceCacheConsumer : IConsume<PriceChanged>, IOnSubscriptionEstablished
    {
        public ValueTask ConsumeAsync(ConsumeContext<PriceChanged> context, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;

        public ValueTask OnSubscriptionEstablishedAsync(
            SubscriptionEstablishedContext context,
            CancellationToken cancellationToken
        ) => ValueTask.CompletedTask;
    }

    [QueueConsumer("billing.issue-invoice")]
    public sealed class IssueInvoiceConsumer : IConsume<IssueInvoice>
    {
        public ValueTask ConsumeAsync(ConsumeContext<IssueInvoice> context, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }

    // Stands in for the module the source generator emits per assembly.
    private sealed class TestMessagingModule : IMessagingModule
    {
        public static void Register(MessagingCatalogBuilder catalog)
        {
            catalog.AddBusConsumer<PriceCacheConsumer, PriceChanged>(
                "billing.price-cache",
                everyInstance: true,
                typeof(TestFailurePolicy),
                _NoDispatch
            );
            catalog.AddQueueConsumer<IssueInvoiceConsumer, IssueInvoice>("billing.issue-invoice", null, _NoDispatch);
        }
    }
}
