// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Messaging.Runtime;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tests.Helpers;
using Fixture = Headless.Messaging.GeneratedModuleFixture;

namespace Tests.Registration;

/// <summary>
/// Covers how a host keys attribute-declared consumers by identity across modules, and how it tunes and filters them:
/// cross-module identity and Queue ownership conflicts, one subscription per Bus identity, <c>Tune</c> and configuration
/// by identity, and <c>ConsumeOnly</c>.
/// </summary>
public sealed class ConsumerHostControlTests : TestBase
{
    [Fact]
    public void should_register_one_bus_identity_covering_two_messages_under_one_subscription()
    {
        // given
        using var provider = _BuildProvider(services =>
            services.AddHeadlessMessaging(setup => setup.AddModule<Fixture.MessagingModule>())
        );

        // when
        var ledger = provider
            .GetDrainedConsumerRegistry()
            .GetAll()
            .Where(x => x.ConsumerType == typeof(Fixture.LedgerProjection))
            .ToList();
        var subscriptions = provider
            .GetRequiredService<MethodMatcherCache>()
            .GetCandidatesMethodsOfLaneGroupNameGrouped();

        // then
        ledger.Should().HaveCount(2);
        ledger.Should().AllSatisfy(x => x.Group.Should().Be(Fixture.LedgerProjection.Identity));
        subscriptions[new ConsumerGroupKey(Fixture.LedgerProjection.Identity, MessageLane.Bus)]
            .Select(x => x.MessageName)
            .Should()
            .BeEquivalentTo(ledger.Select(x => x.MessageName));
    }

    [Fact]
    public async Task should_consume_both_messages_of_one_bus_identity_through_one_subscription()
    {
        // given
        var factory = new RecordingConsumerClientFactory();
        await using var provider = _BuildTransportProvider(
            factory,
            services =>
            {
                services.ConfigureMessaging(messaging =>
                {
                    messaging.AddModule<Fixture.MessagingModule>();
                    messaging.Message<Fixture.LedgerEntryPosted>("fixture.ledger-entry-posted");
                    messaging.Message<Fixture.LedgerEntryReversed>("fixture.ledger-entry-reversed");
                });
            }
        );
        await provider.GetRequiredService<IBootstrapper>().BootstrapAsync(AbortToken);
        var bus = provider.GetRequiredService<IBus>();

        // when
        await bus.PublishAsync(new Fixture.LedgerEntryPosted("L-1"), cancellationToken: AbortToken);
        await bus.PublishAsync(new Fixture.LedgerEntryReversed("L-1"), cancellationToken: AbortToken);
        var probe = provider.GetRequiredService<Fixture.FixtureProbe>();

        // The every-instance invoice projection's subscription-established hook runs on its own instance at startup,
        // and that instance records its disposal, so only the consumed calls are counted.
        IEnumerable<string> consumed() =>
            probe.Calls.Where(x => !string.Equals(x, "projection disposed", StringComparison.Ordinal));
        await _WaitUntilAsync(() => consumed().Skip(1).Any());

        // then
        consumed().Should().BeEquivalentTo("posted L-1", "reversed L-1");
        factory
            .Created.Where(x => x.Lane == MessageLane.Bus)
            .Select(x => x.SubscriptionName)
            .Distinct(StringComparer.Ordinal)
            .Should()
            .BeEquivalentTo(Fixture.InvoiceProjection.Identity, Fixture.LedgerProjection.Identity);
    }

    [Fact]
    public void should_fail_when_two_modules_declare_one_bus_identity_on_different_classes_for_different_messages()
    {
        // given
        using var provider = _BuildProvider(services =>
            services.AddHeadlessMessaging(setup => setup.AddModule<BillingModule>().AddModule<RivalShipmentModule>())
        );

        // when
        var act = () => provider.GetDrainedConsumerRegistry();

        // then
        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage(
                $"*'{TestConsumers.InvoiceProjection}'*two consumer classes*"
                    + $"{typeof(BillingInvoiceProjection).FullName} in {typeof(BillingModule).FullName}*"
                    + $"{typeof(RivalShipmentProjection).FullName} in {typeof(RivalShipmentModule).FullName}*"
            );
    }

    [Fact]
    public void should_fail_when_two_modules_declare_one_bus_consumer_identity()
    {
        // given
        using var provider = _BuildProvider(services =>
        {
            services.ConfigureMessaging(messaging => messaging.AddModule<RivalInvoiceModule>());
            services.AddHeadlessMessaging(setup => setup.AddModule<BillingModule>());
        });

        // when
        var act = () => provider.GetDrainedConsumerRegistry();

        // then
        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage($"*'{TestConsumers.InvoiceProjection}'*")
            .Where(x => x.Message.Contains(typeof(BillingModule).FullName!, StringComparison.Ordinal))
            .Where(x => x.Message.Contains(typeof(RivalInvoiceModule).FullName!, StringComparison.Ordinal));
    }

    [Fact]
    public void should_merge_an_identical_redeclaration_from_another_module()
    {
        // given
        using var provider = _BuildProvider(services =>
            services.AddHeadlessMessaging(setup => setup.AddModule<BillingModule>().AddModule<MirrorBillingModule>())
        );

        // when
        var consumers = provider.GetDrainedConsumerRegistry().GetAll();

        // then
        consumers.Should().ContainSingle(x => x.ConsumerIdentity == TestConsumers.InvoiceProjection);
    }

    [Fact]
    public void should_fail_when_two_modules_declare_queue_consumers_for_one_message()
    {
        // given
        using var provider = _BuildProvider(services =>
            services.AddHeadlessMessaging(setup => setup.AddModule<OrdersQueueModule>().AddModule<BillingQueueModule>())
        );

        // when
        var act = () => provider.GetDrainedConsumerRegistry();

        // then
        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage(
                "Queue message*has two consumers:*"
                    + $"'{TestConsumers.OrdersIssueInvoice}'*{typeof(OrdersQueueModule).FullName}*"
                    + $"'{TestConsumers.BillingIssueInvoice}'*{typeof(BillingQueueModule).FullName}*"
            );
    }

    [Fact]
    public async Task should_run_tuned_middleware_only_for_the_tuned_consumer()
    {
        // given
        await using var provider = _BuildProvider(services =>
        {
            services.ConfigureMessaging(messaging =>
                messaging.Tune(TestConsumers.InvoiceProjection, consumer => consumer.UseMiddleware<AuditMiddleware>())
            );
            services.AddHeadlessMessaging(setup => setup.AddModule<BillingModule>().AddModule<OrdersModule>());
        });
        var invoker = provider.GetRequiredService<ISubscribeInvoker>();

        // when
        await invoker.InvokeAsync(
            _Delivery(_Descriptor(provider, TestConsumers.InvoiceProjection), new InvoiceIssued("INV-1")),
            AbortToken
        );
        await invoker.InvokeAsync(
            _Delivery(_Descriptor(provider, TestConsumers.Shipment), new OrderShipped("ORD-1")),
            AbortToken
        );

        // then
        provider
            .GetRequiredService<HostControlProbe>()
            .Calls.Should()
            .Equal("audit before InvoiceIssued", "billing INV-1", "audit after InvoiceIssued", "orders ORD-1");
    }

    [Fact]
    public void should_apply_tuned_concurrency_and_failure_policy_to_every_message_of_the_identity()
    {
        // given
        using var provider = _BuildProvider(services =>
            services.AddHeadlessMessaging(setup =>
                setup
                    .AddModule<Fixture.MessagingModule>()
                    .Tune(
                        Fixture.LedgerProjection.Identity,
                        consumer => consumer.Concurrency(8).FailurePolicy<Fixture.FixtureFailurePolicy>()
                    )
            )
        );

        // when
        var consumers = provider.GetDrainedConsumerRegistry().GetAll();

        // then
        consumers
            .Where(x => x.ConsumerType == typeof(Fixture.LedgerProjection))
            .Should()
            .HaveCount(2)
            .And.AllSatisfy(x =>
            {
                x.Concurrency.Should().Be(8);
                x.FailurePolicy.Should().Be<Fixture.FixtureFailurePolicy>();
            });
        consumers.Single(x => x.ConsumerType == typeof(Fixture.IssueInvoice)).Concurrency.Should().Be(1);
    }

    [Fact]
    public void should_fail_startup_when_tune_names_an_unknown_identity()
    {
        // given
        using var provider = _BuildProvider(services =>
            services.AddHeadlessMessaging(setup =>
                setup.AddModule<BillingModule>().Tune("billing.unknown", consumer => consumer.Concurrency(2))
            )
        );

        // when
        var act = () => provider.GetDrainedConsumerRegistry();

        // then
        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*Tune names consumer 'billing.unknown', which no registered consumer declares*");
    }

    [Fact]
    public void should_fail_startup_when_tune_names_an_identity_and_no_consumer_is_registered()
    {
        // given
        using var provider = _BuildProvider(services =>
            services.AddHeadlessMessaging(setup => setup.Tune("billing.unknown", consumer => consumer.Concurrency(2)))
        );

        // when
        var act = () => provider.GetDrainedConsumerRegistry();

        // then
        act.Should().Throw<InvalidOperationException>().WithMessage("*'billing.unknown'*");
    }

    [Fact]
    public void should_apply_concurrency_bound_from_configuration_after_tune()
    {
        // given
        using var provider = _BuildProvider(services =>
        {
            services.AddSingleton<IConfiguration>(
                _Configuration(($"Headless:Messaging:Consumers:{TestConsumers.InvoiceProjection}:Concurrency", "16"))
            );
            services.AddHeadlessMessaging(setup =>
                setup
                    .AddModule<BillingModule>()
                    .AddModule<OrdersModule>()
                    .Tune(TestConsumers.InvoiceProjection, consumer => consumer.Concurrency(4))
            );
        });

        // when
        var consumers = provider.GetDrainedConsumerRegistry().GetAll();
        var limit = provider
            .GetRequiredService<MethodMatcherCache>()
            .GetGroupConcurrentLimit(new ConsumerGroupKey(TestConsumers.InvoiceProjection, MessageLane.Bus));

        // then
        consumers
            .Single(x => string.Equals(x.ConsumerIdentity, TestConsumers.InvoiceProjection, StringComparison.Ordinal))
            .Concurrency.Should()
            .Be(16);
        consumers
            .Single(x => string.Equals(x.ConsumerIdentity, TestConsumers.Shipment, StringComparison.Ordinal))
            .Concurrency.Should()
            .Be(1);
        limit.Should().Be(16);
    }

    [Theory]
    [InlineData("Headless:Messaging:Consumers:billing.invoice-projection:Concurrency", "0", "*1 to 255*")]
    [InlineData("Headless:Messaging:Consumers:billing.invoice-projection:Concurrency", "many", "*1 to 255*")]
    [InlineData("Headless:Messaging:Consumers:billing.invoice-projection:Group", "x", "*not a consumer setting*")]
    [InlineData("Headless:Messaging:Consumers:billing.unknown:Concurrency", "2", "*'billing.unknown'*")]
    public void should_fail_startup_when_configuration_tuning_is_invalid(string key, string value, string expected)
    {
        // given
        using var provider = _BuildProvider(services =>
        {
            services.AddSingleton<IConfiguration>(_Configuration((key, value)));
            services.AddHeadlessMessaging(setup => setup.AddModule<BillingModule>());
        });

        // when
        var act = () => provider.GetDrainedConsumerRegistry();

        // then
        act.Should().Throw<InvalidOperationException>().WithMessage(expected);
    }

    [Fact]
    public async Task should_start_no_client_for_consumers_outside_consume_only_and_still_publish_their_messages()
    {
        // given
        var factory = new RecordingConsumerClientFactory();
        await using var provider = _BuildTransportProvider(
            factory,
            services =>
                services.ConfigureMessaging(messaging =>
                {
                    messaging.AddModule<BillingModule>().AddModule<OrdersModule>();
                    messaging.Message<InvoiceIssued>("billing.invoice-issued");
                    messaging.Message<OrderShipped>("orders.order-shipped");
                }),
            setup => setup.ConsumeOnly("orders.*")
        );
        await provider.GetRequiredService<IBootstrapper>().BootstrapAsync(AbortToken);
        var bus = provider.GetRequiredService<IBus>();
        var probe = provider.GetRequiredService<HostControlProbe>();

        // when
        var publishInvoice = async () =>
            await bus.PublishAsync(new InvoiceIssued("INV-9"), cancellationToken: AbortToken);
        await publishInvoice.Should().NotThrowAsync();
        await bus.PublishAsync(new OrderShipped("ORD-9"), cancellationToken: AbortToken);
        await _WaitUntilAsync(() => probe.Calls.Contains("orders ORD-9", StringComparer.Ordinal));

        // then
        factory.Created.Select(x => x.SubscriptionName).Should().OnlyContain(x => x == TestConsumers.Shipment);
        probe.Calls.Should().Equal("orders ORD-9");
        provider
            .GetRequiredService<ConsumerRegistry>()
            .GetAll()
            .Should()
            .Contain(
                x => x.ConsumerIdentity == TestConsumers.InvoiceProjection,
                "a filtered consumer stays registered"
            );
    }

    [Fact]
    public void should_fail_startup_when_a_consume_only_entry_matches_no_consumer()
    {
        // given
        using var provider = _BuildProvider(services =>
            services.AddHeadlessMessaging(setup =>
                setup.AddModule<BillingModule>().ConsumeOnly(TestConsumers.InvoiceProjection, "shipping.*")
            )
        );

        // when
        var act = () => provider.GetDrainedConsumerRegistry();

        // then
        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("Messaging ConsumeOnly entries match no registered consumer: 'shipping.*'.*");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" orders.*")]
    [InlineData("orders.ship*")]
    [InlineData("*.shipment")]
    [InlineData("orders.shipping.*")]
    public void should_reject_a_malformed_consume_only_entry(string entry)
    {
        // given
        var services = new ServiceCollection();

        // when
        var act = () => services.AddHeadlessMessaging(setup => setup.ConsumeOnly(entry));

        // then
        act.Should().Throw<ArgumentException>();
    }

    private static ServiceProvider _BuildProvider(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<Fixture.FixtureProbe>();
        services.AddSingleton<HostControlProbe>();
        configure(services);

        return services.BuildServiceProvider();
    }

    // A host on the in-memory transport and storage whose consumer clients are recorded, so a test can see which
    // subscriptions the host starts.
    private ServiceProvider _BuildTransportProvider(
        RecordingConsumerClientFactory factory,
        Action<IServiceCollection> configure,
        Action<MessagingSetupBuilder>? configureMessaging = null
    )
    {
        return _BuildProvider(services =>
        {
            services.AddLogging(builder => builder.AddProvider(LoggerProvider));
            configure(services);
            services.AddHeadlessMessaging(setup =>
            {
                setup.UseInMemory();
                setup.UseProcessLocalInMemoryStorage();
                configureMessaging?.Invoke(setup);
            });

            var inner = services.Single(x => x.ServiceType == typeof(IConsumerClientFactory));
            services.Remove(inner);
            services.AddSingleton<IConsumerClientFactory>(sp =>
                factory.Wrap((IConsumerClientFactory)ActivatorUtilities.CreateInstance(sp, inner.ImplementationType!))
            );
        });
    }

    private static IConfiguration _Configuration(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(x => new KeyValuePair<string, string?>(x.Key, x.Value)))
            .Build();

    private static async Task _WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        while (!condition())
        {
            await Task.Delay(20, timeout.Token);
        }
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
                [Headers.MessageId] = Guid.NewGuid().ToString(),
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

    private sealed class RecordingConsumerClientFactory
    {
        private readonly Lock _lock = new();
        private readonly List<ConsumerClientRequest> _created = [];

        public IReadOnlyList<ConsumerClientRequest> Created
        {
            get
            {
                lock (_lock)
                {
                    return [.. _created];
                }
            }
        }

        public IConsumerClientFactory Wrap(IConsumerClientFactory inner) => new Recorder(this, inner);

        private sealed class Recorder(RecordingConsumerClientFactory owner, IConsumerClientFactory inner)
            : IConsumerClientFactory
        {
            public Task<IConsumerClient> CreateAsync(
                ConsumerClientRequest request,
                CancellationToken cancellationToken = default
            )
            {
                lock (owner._lock)
                {
                    owner._created.Add(request);
                }

                return inner.CreateAsync(request, cancellationToken);
            }
        }
    }
}
