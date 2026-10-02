// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Headless.Messaging.Runtime;
using Headless.Reliability;
using Headless.Testing.Tests;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.Registration;

/// <summary>
/// Covers how a host resolves each competing consumer's failure policy: <c>Tune</c> over the declaration over the host
/// default, then configuration overriding the numeric fields, and the rejections that keep a policy off every-instance
/// consumers and stop two modules from declaring one consumer with different policies.
/// </summary>
public sealed class ConsumerFailurePolicyResolutionTests : TestBase
{
    private const string _Invoice = TestConsumers.InvoiceProjection;
    private const string _PolicyPath = $"Headless:Messaging:Consumers:{_Invoice}:FailurePolicy";

    [Fact]
    public void should_resolve_the_declared_policy_when_nothing_tunes_it()
    {
        // given
        using var provider = _BuildProvider(setup => setup.AddModule<StrictBillingModule>());

        // when
        var policy = _Resolved(provider, _Invoice);

        // then
        policy.ImmediateRetries.Should().Be(1);
        policy.DelayedRetries.Should().Be(3);
        policy.DelayedInitialDelay.Should().Be(TimeSpan.FromSeconds(10));
        policy.DelayedMaxDelay.Should().Be(TimeSpan.FromMinutes(2));
        policy.ShouldFail(new CardDeclinedException()).Should().BeTrue();
    }

    [Fact]
    public void should_copy_the_resolved_policy_onto_the_consumer_descriptor()
    {
        // given
        using var provider = _BuildProvider(setup => setup.AddModule<StrictBillingModule>());

        // when
        var descriptor = provider
            .GetRequiredService<IConsumerServiceSelector>()
            .SelectCandidates()
            .Single(x => string.Equals(x.ConsumerIdentity, _Invoice, StringComparison.Ordinal));

        // then
        descriptor.FailurePolicy.Should().BeSameAs(_Resolved(provider, _Invoice));
        descriptor.FailurePolicy.ImmediateRetries.Should().Be(1);
    }

    [Fact]
    public void should_let_a_tuned_policy_type_replace_the_declared_policy()
    {
        // given
        using var provider = _BuildProvider(setup =>
        {
            setup.AddModule<StrictBillingModule>();
            setup.Tune(_Invoice, consumer => consumer.FailurePolicy<LenientPolicy>());
        });

        // when
        var policy = _Resolved(provider, _Invoice);

        // then
        policy.ImmediateRetries.Should().Be(4);
        policy.DelayedRetries.Should().Be(0);
        policy.ShouldFail(new CardDeclinedException()).Should().BeFalse();
    }

    [Fact]
    public void should_let_a_tuned_inline_policy_replace_the_declared_policy()
    {
        // given
        using var provider = _BuildProvider(setup =>
        {
            setup.AddModule<StrictBillingModule>();
            setup.Tune(_Invoice, consumer => consumer.FailurePolicy(policy => policy.Immediate(7)));
        });

        // when
        var policy = _Resolved(provider, _Invoice);

        // then
        policy.ImmediateRetries.Should().Be(7);
        policy.DelayedRetries.Should().Be(0);
    }

    [Fact]
    public void should_override_only_the_configured_numbers_and_keep_the_declared_fail_rules()
    {
        // given
        using var provider = _BuildProvider(
            setup => setup.AddModule<StrictBillingModule>(),
            _Configuration(($"{_PolicyPath}:ImmediateRetries", "0"), ($"{_PolicyPath}:DelayedMaxDelay", "00:05:00"))
        );

        // when
        var policy = _Resolved(provider, _Invoice);

        // then
        policy.ImmediateRetries.Should().Be(0);
        policy.DelayedRetries.Should().Be(3);
        policy.DelayedInitialDelay.Should().Be(TimeSpan.FromSeconds(10));
        policy.DelayedMaxDelay.Should().Be(TimeSpan.FromMinutes(5));
        policy.ShouldFail(new CardDeclinedException()).Should().BeTrue();
    }

    [Fact]
    public void should_apply_configuration_over_a_tuned_policy()
    {
        // given
        using var provider = _BuildProvider(
            setup =>
            {
                setup.AddModule<StrictBillingModule>();
                setup.Tune(_Invoice, consumer => consumer.FailurePolicy<LenientPolicy>());
            },
            _Configuration(
                ($"{_PolicyPath}:DelayedRetries", "2"),
                ($"{_PolicyPath}:DelayedInitialDelay", "00:00:05"),
                ($"{_PolicyPath}:DelayedMaxDelay", "00:01:00")
            )
        );

        // when
        var policy = _Resolved(provider, _Invoice);

        // then
        policy.ImmediateRetries.Should().Be(4);
        policy.DelayedRetries.Should().Be(2);
        policy.DelayedInitialDelay.Should().Be(TimeSpan.FromSeconds(5));
        policy.DelayedMaxDelay.Should().Be(TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void should_resolve_the_framework_default_for_an_undeclared_untuned_consumer()
    {
        // given
        using var provider = _BuildProvider(setup => setup.AddModule<BillingModule>());

        // when
        var policy = _Resolved(provider, _Invoice);

        // then
        policy.ImmediateRetries.Should().Be(2);
        policy.DelayedRetries.Should().Be(5);
        policy.DelayedInitialDelay.Should().Be(TimeSpan.FromSeconds(30));
        policy.DelayedMaxDelay.Should().Be(TimeSpan.FromMinutes(15));
        policy.FailOnExceptionTypes.Should().BeEmpty();
        policy.FailWhenRuleCount.Should().Be(0);
    }

    [Fact]
    public void should_resolve_the_host_default_type_for_an_undeclared_consumer()
    {
        // given
        using var provider = _BuildProvider(setup =>
        {
            setup.AddModule<BillingModule>();
            setup.DefaultFailurePolicy<LenientPolicy>();
        });

        // when
        var policy = _Resolved(provider, _Invoice);

        // then
        policy.ImmediateRetries.Should().Be(4);
        policy.DelayedRetries.Should().Be(0);
    }

    [Fact]
    public void should_resolve_an_inline_host_default_and_let_configuration_override_it()
    {
        // given
        using var provider = _BuildProvider(
            setup =>
            {
                setup.AddModule<BillingModule>();
                setup.DefaultFailurePolicy(policy =>
                    policy.Immediate(1).Delayed(3, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(10))
                );
            },
            _Configuration(($"{_PolicyPath}:DelayedRetries", "6"))
        );

        // when
        var policy = _Resolved(provider, _Invoice);

        // then
        policy.ImmediateRetries.Should().Be(1);
        policy.DelayedRetries.Should().Be(6);
        policy.DelayedInitialDelay.Should().Be(TimeSpan.FromMinutes(1));
        policy.DelayedMaxDelay.Should().Be(TimeSpan.FromMinutes(10));
    }

    [Fact]
    public void should_keep_the_declared_policy_over_the_host_default()
    {
        // given
        using var provider = _BuildProvider(setup =>
        {
            setup.AddModule<StrictBillingModule>();
            setup.DefaultFailurePolicy<LenientPolicy>();
        });

        // when
        var policy = _Resolved(provider, _Invoice);

        // then
        policy.ImmediateRetries.Should().Be(1);
    }

    [Fact]
    public void should_build_one_definition_for_every_message_of_one_identity()
    {
        // given
        using var provider = _BuildProvider(setup => setup.AddModule<StrictLedgerModule>());

        // when
        var policies = provider
            .GetRequiredService<ConsumerRegistry>()
            .GetAll()
            .Where(x => string.Equals(x.ConsumerIdentity, StrictLedgerModule.Identity, StringComparison.Ordinal))
            .Select(x => x.FailurePolicy)
            .ToList();

        // then
        policies.Should().HaveCount(2);
        policies[0].Should().NotBeNull().And.BeSameAs(policies[1]);
    }

    [Fact]
    public void should_give_an_every_instance_consumer_no_failure_policy()
    {
        // given
        using var provider = _BuildProvider(setup => setup.AddModule<BillingPriceCacheModule>());

        // when
        var consumer = provider
            .GetRequiredService<ConsumerRegistry>()
            .GetAll()
            .Single(x => string.Equals(x.ConsumerIdentity, TestConsumers.PriceCache, StringComparison.Ordinal));
        var descriptor = provider
            .GetRequiredService<IConsumerServiceSelector>()
            .SelectCandidates()
            .Single(x => string.Equals(x.ConsumerIdentity, TestConsumers.PriceCache, StringComparison.Ordinal));

        // then
        consumer.FailurePolicy.Should().BeNull();
        descriptor.FailurePolicy.Should().BeSameAs(FailurePolicyDefinition.None);
    }

    [Theory]
    [InlineData("Attempts", "3", "*'" + _PolicyPath + ":Attempts' is not a failure policy setting*")]
    [InlineData("ImmediateRetries", "many", "*'" + _PolicyPath + ":ImmediateRetries'*integer*")]
    [InlineData("DelayedRetries", "-1", "*'" + _PolicyPath + "'*")]
    [InlineData("DelayedInitialDelay", "soon", "*'" + _PolicyPath + ":DelayedInitialDelay'*duration*")]
    [InlineData("DelayedMaxDelay", "00:00:01", "*'" + _PolicyPath + "'*")]
    public void should_fail_startup_when_configured_failure_policy_is_invalid(string key, string value, string expected)
    {
        // given
        using var provider = _BuildProvider(
            setup => setup.AddModule<StrictBillingModule>(),
            _Configuration(($"{_PolicyPath}:{key}", value))
        );

        // when
        var act = () => provider.GetRequiredService<ConsumerRegistry>();

        // then
        act.Should().Throw<InvalidOperationException>().WithMessage("Messaging tuning is invalid:*" + expected);
    }

    [Fact]
    public void should_fail_startup_when_tune_gives_an_every_instance_consumer_a_failure_policy()
    {
        // given
        using var provider = _BuildProvider(setup =>
        {
            setup.AddModule<BillingPriceCacheModule>();
            setup.Tune(TestConsumers.PriceCache, consumer => consumer.FailurePolicy<LenientPolicy>());
        });

        // when
        var act = () => provider.GetRequiredService<ConsumerRegistry>();

        // then
        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage($"*Tune gives every-instance consumer '{TestConsumers.PriceCache}' a failure policy*");
    }

    [Fact]
    public void should_fail_startup_when_configuration_gives_an_every_instance_consumer_a_failure_policy()
    {
        // given
        using var provider = _BuildProvider(
            setup => setup.AddModule<BillingPriceCacheModule>(),
            _Configuration(
                ($"Headless:Messaging:Consumers:{TestConsumers.PriceCache}:FailurePolicy:ImmediateRetries", "1")
            )
        );

        // when
        var act = () => provider.GetRequiredService<ConsumerRegistry>();

        // then
        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage(
                $"*Configuration 'Headless:Messaging:Consumers:{TestConsumers.PriceCache}' gives every-instance "
                    + $"consumer '{TestConsumers.PriceCache}' a failure policy*"
            );
    }

    [Fact]
    public void should_fail_startup_when_a_module_declares_a_failure_policy_on_an_every_instance_consumer()
    {
        // given
        using var provider = _BuildProvider(setup => setup.AddModule<EveryInstancePolicyModule>());

        // when
        var act = () => provider.GetRequiredService<ConsumerRegistry>();

        // then
        act.Should()
            .Throw<ArgumentException>()
            .WithMessage(
                $"*{typeof(EveryInstancePolicyModule).FullName}*every-instance consumer '{TestConsumers.PriceCache}'*"
                    + "failure policy*"
            );
    }

    [Fact]
    public void should_fail_startup_when_two_modules_declare_one_consumer_with_different_policies()
    {
        // given
        using var provider = _BuildProvider(setup =>
            setup.AddModule<StrictBillingModule>().AddModule<LenientBillingModule>()
        );

        // when
        var act = () => provider.GetRequiredService<ConsumerRegistry>();

        // then
        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage($"*{typeof(StrictPolicy).FullName}*{typeof(LenientPolicy).FullName}*")
            .Where(x => x.Message.Contains(typeof(StrictBillingModule).FullName!, StringComparison.Ordinal))
            .Where(x => x.Message.Contains(typeof(LenientBillingModule).FullName!, StringComparison.Ordinal));
    }

    [Fact]
    public void should_fail_startup_when_one_identity_declares_different_policies_for_its_messages()
    {
        // given
        using var provider = _BuildProvider(setup => setup.AddModule<SplitPolicyLedgerModule>());

        // when
        var act = () => provider.GetRequiredService<ConsumerRegistry>();

        // then
        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage(
                $"*'{StrictLedgerModule.Identity}'*{typeof(StrictPolicy).FullName}*{typeof(LenientPolicy).FullName}*"
            );
    }

    [Fact]
    public void should_give_a_runtime_subscription_the_host_default()
    {
        // given
        using var provider = _BuildProvider(setup => setup.DefaultFailurePolicy<LenientPolicy>());
        var registry = provider.GetRequiredService<IRuntimeConsumerRegistry>();

        // when
        registry.Register<InvoiceIssued>(new RuntimeInvoiceHandler().HandleAsync);

        // then
        var descriptor = registry.GetDescriptors().Should().ContainSingle().Subject;
        descriptor.FailurePolicy.ImmediateRetries.Should().Be(4);
        descriptor.FailurePolicy.DelayedRetries.Should().Be(0);
    }

    [Fact]
    public void should_give_a_runtime_subscription_the_framework_default_when_the_host_sets_none()
    {
        // given
        using var provider = _BuildProvider(_ => { });
        var registry = provider.GetRequiredService<IRuntimeConsumerRegistry>();

        // when
        registry.Register<InvoiceIssued>(new RuntimeInvoiceHandler().HandleAsync);

        // then
        var policy = registry.GetDescriptors().Should().ContainSingle().Subject.FailurePolicy;
        policy.ImmediateRetries.Should().Be(2);
        policy.DelayedRetries.Should().Be(5);
    }

    private static FailurePolicyDefinition _Resolved(IServiceProvider provider, string identity) =>
        provider
            .GetRequiredService<ConsumerRegistry>()
            .GetAll()
            .Single(x => string.Equals(x.ConsumerIdentity, identity, StringComparison.Ordinal))
            .FailurePolicy!;

    private static ServiceProvider _BuildProvider(
        Action<MessagingSetupBuilder> configure,
        IConfiguration? configuration = null
    )
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<HostControlProbe>();
        if (configuration is not null)
        {
            services.AddSingleton(configuration);
        }

        services.AddHeadlessMessaging(setup =>
        {
            setup.UseInMemory();
            setup.UseProcessLocalInMemoryStorage();
            configure(setup);
        });

        return services.BuildServiceProvider();
    }

    private static IConfiguration _Configuration(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(x => new KeyValuePair<string, string?>(x.Key, x.Value)))
            .Build();

    public sealed class CardDeclinedException : Exception;

    public sealed class StrictPolicy : FailurePolicy
    {
        protected override void Configure(FailurePolicyBuilder policy) =>
            policy
                .Immediate(1)
                .Delayed(3, TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(2))
                .FailOn<CardDeclinedException>();
    }

    public sealed class LenientPolicy : FailurePolicy
    {
        protected override void Configure(FailurePolicyBuilder policy) => policy.Immediate(4);
    }

    public sealed class LedgerProjection : IConsume<InvoiceIssued>, IConsume<OrderShipped>
    {
        public ValueTask ConsumeAsync(ConsumeContext<InvoiceIssued> context, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;

        public ValueTask ConsumeAsync(ConsumeContext<OrderShipped> context, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }

    public sealed class RuntimeInvoiceHandler
    {
#pragma warning disable IDE0060 // False positive: the parameters are fixed by the RuntimeConsumeHandler delegate the method is bound to.
        public ValueTask HandleAsync(
            ConsumeContext<InvoiceIssued> context,
            IServiceProvider services,
            CancellationToken cancellationToken
        ) => ValueTask.CompletedTask;
#pragma warning restore IDE0060
    }

    public sealed class StrictBillingModule : IMessagingModule
    {
        public static void Register(MessagingCatalogBuilder catalog) =>
            catalog.AddBusConsumer<BillingInvoiceProjection, InvoiceIssued>(
                _Invoice,
                everyInstance: false,
                TestConsumers.Dispatch<BillingInvoiceProjection, InvoiceIssued>(),
                failurePolicy: static () => new StrictPolicy()
            );
    }

    public sealed class LenientBillingModule : IMessagingModule
    {
        public static void Register(MessagingCatalogBuilder catalog) =>
            catalog.AddBusConsumer<BillingInvoiceProjection, InvoiceIssued>(
                _Invoice,
                everyInstance: false,
                TestConsumers.Dispatch<BillingInvoiceProjection, InvoiceIssued>(),
                failurePolicy: static () => new LenientPolicy()
            );
    }

    public sealed class StrictLedgerModule : IMessagingModule
    {
        public const string Identity = "billing.ledger";

        public static void Register(MessagingCatalogBuilder catalog)
        {
            catalog.AddBusConsumer<LedgerProjection, InvoiceIssued>(
                Identity,
                everyInstance: false,
                TestConsumers.Dispatch<LedgerProjection, InvoiceIssued>(),
                failurePolicy: static () => new StrictPolicy()
            );
            catalog.AddBusConsumer<LedgerProjection, OrderShipped>(
                Identity,
                everyInstance: false,
                TestConsumers.Dispatch<LedgerProjection, OrderShipped>(),
                failurePolicy: static () => new StrictPolicy()
            );
        }
    }

    public sealed class SplitPolicyLedgerModule : IMessagingModule
    {
        public static void Register(MessagingCatalogBuilder catalog)
        {
            catalog.AddBusConsumer<LedgerProjection, InvoiceIssued>(
                StrictLedgerModule.Identity,
                everyInstance: false,
                TestConsumers.Dispatch<LedgerProjection, InvoiceIssued>(),
                failurePolicy: static () => new StrictPolicy()
            );
            catalog.AddBusConsumer<LedgerProjection, OrderShipped>(
                StrictLedgerModule.Identity,
                everyInstance: false,
                TestConsumers.Dispatch<LedgerProjection, OrderShipped>(),
                failurePolicy: static () => new LenientPolicy()
            );
        }
    }

    public sealed class EveryInstancePolicyModule : IMessagingModule
    {
        public static void Register(MessagingCatalogBuilder catalog) =>
            catalog.AddBusConsumer<BillingPriceCache, PriceChanged>(
                TestConsumers.PriceCache,
                everyInstance: true,
                TestConsumers.Dispatch<BillingPriceCache, PriceChanged>(),
                failurePolicy: static () => new LenientPolicy()
            );
    }
}
