// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;
using Headless.Messaging;
using Headless.Messaging.CircuitBreaker;
using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Headless.UnitOfWork;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class MessagingBuilderTests
{
    [Fact]
    public void should_use_message_type_name_as_default_topic()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.AddHeadlessMessaging(static setup => setup.AddConsumer<TestOrderConsumer>());

        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<ConsumerRegistry>();

        // then
        var orderConsumer = registry.GetAll().First(c => c.ConsumerType == typeof(TestOrderConsumer));
        orderConsumer.MessageName.Should().Be(nameof(TestOrderMessage));
    }

    [Fact]
    public async Task should_register_runtime_and_bootstrap_services()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();

        // when
        services.AddHeadlessMessaging(messaging =>
        {
            messaging.UseInMemory();
            messaging.UseProcessLocalInMemoryStorage();
        });

        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        // then — IBus/IQueue are scoped: resolve from a created scope, never the root provider.
        provider.GetRequiredService<IRuntimeSubscriber>().Should().NotBeNull();
        provider.GetRequiredService<IBootstrapper>().Should().NotBeNull();
        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<IBus>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<IQueue>().Should().NotBeNull();
    }

    [Fact]
    public void should_register_unit_of_work_manager_exactly_once_regardless_of_call_order()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.AddHeadlessMessaging(_ => { });
        services.AddUnitOfWork();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();

        // then — AddUnitOfWork() is idempotent, so exactly one IUnitOfWorkFactory registration exists.
        scope.ServiceProvider.GetRequiredService<IUnitOfWorkFactory>().Should().NotBeNull();
    }

    [Fact]
    public void should_prevent_duplicate_topic_mappings()
    {
        // given
        var services = new ServiceCollection();
        services.AddHeadlessMessaging(messaging =>
        {
            messaging.WithMessageNameMapping<TestOrderMessage>("orders.placed");
            messaging.WithMessageNameMapping<TestOrderMessage>("orders.created");
        });
        using var provider = services.BuildServiceProvider();

        // when
        var act = () => provider.GetRequiredService<IConsumerRegistry>();

        // then
        act.Should().Throw<InvalidOperationException>().WithMessage("*already mapped to messageName*");
    }

    [Fact]
    public void should_allow_same_topic_mapping_if_identical()
    {
        // given
        var services = new ServiceCollection();
        services.AddHeadlessMessaging(messaging =>
        {
            messaging.WithMessageNameMapping<TestOrderMessage>("orders.placed");
            messaging.WithMessageNameMapping<TestOrderMessage>("orders.placed");
        });
        using var provider = services.BuildServiceProvider();

        // when
        var act = () => provider.GetRequiredService<IConsumerRegistry>();

        // then
        act.Should().NotThrow();
    }

    [Fact]
    public void should_replace_messaging_lock_provider_when_use_distributed_lock_called_twice()
    {
        // given — last-wins semantics: second registration must supersede the first
        var services = new ServiceCollection();
        var builder = new MessagingBuilder(services);
        var firstProvider = Substitute.For<IDistributedLock>();
        var secondProvider = Substitute.For<IDistributedLock>();

        // when
        builder.UseDistributedLock(firstProvider);
        builder.UseDistributedLock(secondProvider);

        // then — only one descriptor present for the messaging-keyed slot
        var descriptors = services
            .Where(d =>
                d.ServiceType == typeof(IDistributedLock)
                && d.IsKeyedService
                && Equals(d.ServiceKey, MessagingKeys.LockProvider)
            )
            .ToArray();

        descriptors.Should().ContainSingle();

        using var provider = services.BuildServiceProvider();
        var resolved = provider.GetRequiredKeyedService<IDistributedLock>(MessagingKeys.LockProvider);
        resolved.Should().BeSameAs(secondProvider, "the second registration must win under last-wins semantics");
    }
}

public sealed record TestOrderMessage(string OrderId, decimal Amount);

public sealed record TestPaymentMessage(string PaymentId, decimal Amount);

[BusConsumer("tests.messaging-builder.orders")]
public sealed class TestOrderConsumer : IConsume<TestOrderMessage>
{
    public ValueTask ConsumeAsync(ConsumeContext<TestOrderMessage> context, CancellationToken cancellationToken)
    {
        return ValueTask.CompletedTask;
    }
}

public sealed class AnotherOrderConsumer : IConsume<TestOrderMessage>
{
    public ValueTask ConsumeAsync(ConsumeContext<TestOrderMessage> context, CancellationToken cancellationToken)
    {
        return ValueTask.CompletedTask;
    }
}

public sealed class TestPaymentConsumer : IConsume<TestPaymentMessage>
{
    public ValueTask ConsumeAsync(ConsumeContext<TestPaymentMessage> context, CancellationToken cancellationToken)
    {
        return ValueTask.CompletedTask;
    }
}

public sealed class MultiMessageConsumer : IConsume<TestOrderMessage>, IConsume<TestPaymentMessage>
{
    public ValueTask ConsumeAsync(ConsumeContext<TestOrderMessage> context, CancellationToken cancellationToken)
    {
        return ValueTask.CompletedTask;
    }

    public ValueTask ConsumeAsync(ConsumeContext<TestPaymentMessage> context, CancellationToken cancellationToken)
    {
        return ValueTask.CompletedTask;
    }
}
