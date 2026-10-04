// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
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
    public void should_read_the_declared_identity_and_every_instance()
    {
        var attribute = (BusConsumerAttribute)
            Attribute.GetCustomAttribute(typeof(PriceCacheConsumer), typeof(MessageConsumerAttribute))!;

        attribute.Identity.Should().Be("billing.price-cache");
        attribute.EveryInstance.Should().BeTrue();
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
                    _NoDispatch,
                    _NoHook
                ),
                new MessagingConsumerDeclaration(
                    typeof(TestMessagingModule).FullName!,
                    typeof(IssueInvoiceConsumer),
                    typeof(IssueInvoice),
                    MessageLane.Queue,
                    "billing.issue-invoice",
                    EveryInstance: false,
                    _NoDispatch,
                    OnSubscriptionEstablished: null
                ),
            ]);
    }

    [Fact]
    public void should_declare_a_responder_on_the_queue_lane_with_its_response_type()
    {
        // given
        var catalog = new MessagingCatalogBuilder();

        // when
        catalog.AddQueueResponder<GetQuoteResponder, GetQuote, Quote>("pricing.get-quote", _NoDispatch);

        // then
        var declaration = catalog.Consumers.Should().ContainSingle().Subject;
        declaration.ConsumerType.Should().Be<GetQuoteResponder>();
        declaration.MessageType.Should().Be<GetQuote>();
        declaration.ResponseType.Should().Be<Quote>();
        declaration.Lane.Should().Be(MessageLane.Queue);
        declaration.Identity.Should().Be("pricing.get-quote");
        declaration.EveryInstance.Should().BeFalse();
        declaration.OnSubscriptionEstablished.Should().BeNull();
    }

    [Fact]
    public void should_reject_a_responder_without_a_dispatch()
    {
        var catalog = new MessagingCatalogBuilder();

        var add = () => catalog.AddQueueResponder<GetQuoteResponder, GetQuote, Quote>("pricing.get-quote", null!);

        add.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("")]
    public void should_reject_an_empty_identity(string identity)
    {
        var catalog = new MessagingCatalogBuilder();

        var add = () =>
            catalog.AddBusConsumer<PriceCacheConsumer, PriceChanged>(identity, everyInstance: false, _NoDispatch);

        add.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void should_reject_an_identity_longer_than_durable_storage_holds()
    {
        var catalog = new MessagingCatalogBuilder();
        var identity = "billing." + new string('x', ConsumerMetadata.ConsumerIdentityMaxLength);

        var add = () =>
            catalog.AddBusConsumer<PriceCacheConsumer, PriceChanged>(identity, everyInstance: false, _NoDispatch);

        add.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void should_reject_a_missing_dispatch()
    {
        var catalog = new MessagingCatalogBuilder();

        var add = () => catalog.AddQueueConsumer<IssueInvoiceConsumer, IssueInvoice>("billing.issue-invoice", null!);

        add.Should().Throw<ArgumentNullException>();
    }

    // Stands in for a generated dispatcher; these tests only read the declarations.
    private static readonly MessageConsumerDispatch _NoDispatch = static (_, _, _) => ValueTask.CompletedTask;

    // Stands in for a generated subscription hook.
    private static readonly SubscriptionEstablishedDispatch _NoHook = static (_, _, _) => ValueTask.CompletedTask;

    public sealed record PriceChanged(string Sku);

    public sealed record IssueInvoice(string OrderId);

    [BusConsumer("billing.price-cache", EveryInstance = true)]
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

    public sealed record GetQuote(string Sku);

    public sealed record Quote(decimal Price);

    public sealed class GetQuoteResponder : IRespond<GetQuote, Quote>
    {
        public ValueTask<Quote> RespondAsync(ConsumeContext<GetQuote> context, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new Quote(1m));
    }

    // Stands in for the module the source generator emits per assembly.
    private sealed class TestMessagingModule : IMessagingModule
    {
        public static void Register(MessagingCatalogBuilder catalog)
        {
            catalog.AddBusConsumer<PriceCacheConsumer, PriceChanged>(
                "billing.price-cache",
                everyInstance: true,
                _NoDispatch,
                _NoHook
            );
            catalog.AddQueueConsumer<IssueInvoiceConsumer, IssueInvoice>("billing.issue-invoice", _NoDispatch);
        }
    }
}
