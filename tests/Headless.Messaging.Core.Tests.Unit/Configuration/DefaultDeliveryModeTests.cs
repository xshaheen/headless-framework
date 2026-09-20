// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Messages;
using Headless.Messaging.Monitoring;
using Headless.Messaging.Persistence;
using Headless.Messaging.Registration;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tests.Configuration;

public sealed class DefaultDeliveryModeTests : TestBase
{
    [Theory]
    [InlineData(MessageLane.Bus, DeliveryMode.Durable)]
    [InlineData(MessageLane.Queue, DeliveryMode.Durable)]
    [InlineData(MessageLane.Bus, DeliveryMode.Direct)]
    [InlineData(MessageLane.Queue, DeliveryMode.Direct)]
    public async Task should_inherit_global_mode_in_all_ordinary_call_forms(MessageLane lane, DeliveryMode mode)
    {
        await using var provider = _CreateProvider(mode);
        await using var scope = provider.CreateAsyncScope();
        provider.GetRequiredService<IOptions<MessagingOptions>>().Value.DefaultDeliveryMode.Should().Be(mode);
        var message = new TestMessage("inherit");
        if (lane == MessageLane.Bus)
        {
            var bus = scope.ServiceProvider.GetRequiredService<IBus>();
            await bus.PublishAsync(message, AbortToken);
            await bus.PublishAsync(message, options: null, AbortToken);
            await bus.PublishAsync(message, new PublishOptions { CorrelationId = "record" }, AbortToken);
            await bus.PublishAsync(message, static options => options.WithCorrelationId("fluent"), AbortToken);
        }
        else
        {
            var queue = scope.ServiceProvider.GetRequiredService<IQueue>();
            await queue.EnqueueAsync(message, AbortToken);
            await queue.EnqueueAsync(message, options: null, AbortToken);
            await queue.EnqueueAsync(message, new QueueOptions { CorrelationId = "record" }, AbortToken);
            await queue.EnqueueAsync(message, static options => options.WithCorrelationId("fluent"), AbortToken);
        }

        await _AssertStoredCountAsync(provider, lane, mode == DeliveryMode.Durable ? 4 : 0);
    }

    [Theory]
    [InlineData(MessageLane.Bus, DeliveryMode.Durable, DeliveryMode.Direct)]
    [InlineData(MessageLane.Queue, DeliveryMode.Durable, DeliveryMode.Direct)]
    [InlineData(MessageLane.Bus, DeliveryMode.Direct, DeliveryMode.Durable)]
    [InlineData(MessageLane.Queue, DeliveryMode.Direct, DeliveryMode.Durable)]
    public async Task should_prioritize_explicit_mode_over_global_mode(
        MessageLane lane,
        DeliveryMode globalMode,
        DeliveryMode explicitMode
    )
    {
        await using var provider = _CreateProvider(globalMode);
        await using var scope = provider.CreateAsyncScope();
        var message = new TestMessage("override");
        if (lane == MessageLane.Bus)
        {
            await scope
                .ServiceProvider.GetRequiredService<IBus>()
                .PublishAsync(message, new PublishOptions { DeliveryMode = explicitMode }, AbortToken);
        }
        else
        {
            await scope
                .ServiceProvider.GetRequiredService<IQueue>()
                .EnqueueAsync(message, new QueueOptions { DeliveryMode = explicitMode }, AbortToken);
        }

        await _AssertStoredCountAsync(provider, lane, explicitMode == DeliveryMode.Durable ? 1 : 0);
    }

    [Fact]
    public async Task should_reject_invalid_global_mode_through_options_validation()
    {
        await using var provider = _CreateProvider((DeliveryMode)99);

        var resolve = () => provider.GetRequiredService<IOptions<MessagingOptions>>().Value;

        resolve.Should().Throw<OptionsValidationException>().WithMessage("*Default Delivery Mode*");
    }

    [Theory]
    [InlineData(MessageLane.Bus, DeliveryMode.Durable, DeliveryMode.Direct)]
    [InlineData(MessageLane.Queue, DeliveryMode.Durable, DeliveryMode.Direct)]
    [InlineData(MessageLane.Bus, DeliveryMode.Direct, DeliveryMode.Durable)]
    [InlineData(MessageLane.Queue, DeliveryMode.Direct, DeliveryMode.Durable)]
    public async Task should_prioritize_type_policy_over_global_mode(
        MessageLane lane,
        DeliveryMode globalMode,
        DeliveryMode typeMode
    )
    {
        await using var provider = _CreateProvider(
            globalMode,
            setup =>
            {
                setup.Bus.ForMessage<TestMessage>(message =>
                    message.Contract("test.default-mode").WithDeliveryMode(typeMode)
                );
                setup.Queue.ForMessage<TestMessage>(message =>
                    message.Contract("test.default-mode").WithDeliveryMode(typeMode)
                );
            }
        );

        await _PublishAsync(provider, lane, new TestMessage("type-policy"), explicitMode: null);

        await _AssertStoredCountAsync(provider, lane, typeMode == DeliveryMode.Durable ? 1 : 0);
    }

    [Theory]
    [InlineData(MessageLane.Bus, DeliveryMode.Durable, DeliveryMode.Direct)]
    [InlineData(MessageLane.Queue, DeliveryMode.Durable, DeliveryMode.Direct)]
    [InlineData(MessageLane.Bus, DeliveryMode.Direct, DeliveryMode.Durable)]
    [InlineData(MessageLane.Queue, DeliveryMode.Direct, DeliveryMode.Durable)]
    public async Task should_prioritize_explicit_mode_over_type_policy(
        MessageLane lane,
        DeliveryMode typeMode,
        DeliveryMode explicitMode
    )
    {
        // The host default agrees with the type policy so only the per-call override can produce the outcome.
        await using var provider = _CreateProvider(
            typeMode,
            setup =>
            {
                setup.Bus.ForMessage<TestMessage>(message =>
                    message.Contract("test.default-mode").WithDeliveryMode(typeMode)
                );
                setup.Queue.ForMessage<TestMessage>(message =>
                    message.Contract("test.default-mode").WithDeliveryMode(typeMode)
                );
            }
        );

        await _PublishAsync(provider, lane, new TestMessage("explicit"), explicitMode);

        await _AssertStoredCountAsync(provider, lane, explicitMode == DeliveryMode.Durable ? 1 : 0);
    }

    [Fact]
    public async Task should_resolve_type_policy_per_lane()
    {
        await using var provider = _CreateProvider(
            DeliveryMode.Durable,
            setup =>
            {
                setup.Bus.ForMessage<TestMessage>(message =>
                    message.Contract("test.default-mode").WithDeliveryMode(DeliveryMode.Direct)
                );
                setup.Queue.ForMessage<TestMessage>(message =>
                    message.Contract("test.default-mode").WithDeliveryMode(DeliveryMode.Durable)
                );
            }
        );

        await _PublishAsync(provider, MessageLane.Bus, new TestMessage("bus"), explicitMode: null);
        await _PublishAsync(provider, MessageLane.Queue, new TestMessage("queue"), explicitMode: null);

        await _AssertStoredCountAsync(provider, MessageLane.Bus, 0);
        await _AssertStoredCountAsync(provider, MessageLane.Queue, 1);
    }

    [Theory]
    [InlineData(MessageLane.Bus)]
    [InlineData(MessageLane.Queue)]
    public async Task should_reject_a_delay_on_a_type_registered_direct_before_any_effect(MessageLane lane)
    {
        await using var provider = _CreateProvider(
            DeliveryMode.Durable,
            setup =>
            {
                setup.Bus.ForMessage<TestMessage>(message =>
                    message.Contract("test.default-mode").WithDeliveryMode(DeliveryMode.Direct)
                );
                setup.Queue.ForMessage<TestMessage>(message =>
                    message.Contract("test.default-mode").WithDeliveryMode(DeliveryMode.Direct)
                );
            }
        );
        var message = new TestMessage("delayed");
        var delay = TimeSpan.FromMinutes(1);

        await using var scope = provider.CreateAsyncScope();
        var act = () =>
            lane == MessageLane.Bus
                ? scope
                    .ServiceProvider.GetRequiredService<IBus>()
                    .PublishAsync(message, new PublishOptions { Delay = delay }, AbortToken)
                : scope
                    .ServiceProvider.GetRequiredService<IQueue>()
                    .EnqueueAsync(message, new QueueOptions { Delay = delay }, AbortToken);

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*Direct delivery cannot specify a delay*");
        await _AssertStoredCountAsync(provider, lane, 0);
    }

    [Fact]
    public async Task should_inherit_global_mode_for_an_assembly_scan_registration()
    {
        await using var provider = _CreateProvider(
            DeliveryMode.Direct,
            setup => setup.Bus.ForConsumersFromAssemblyContaining<DefaultDeliveryModeTests>(_ConfigureScannedConsumer)
        );

        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IBus>().PublishAsync(new ScannedMessage("scan"), AbortToken);

        await _AssertStoredCountAsync(provider, MessageLane.Bus, 0);
    }

    [Fact]
    public async Task should_apply_explicit_type_policy_alongside_assembly_scan_registrations_for_the_same_type()
    {
        // Two scanned consumers share the (ScannedMessage, Bus) key; only the explicit registration carries a policy,
        // so the publisher must build without a key collision and honor the explicit Direct policy.
        await using var provider = _CreateProvider(
            DeliveryMode.Durable,
            setup =>
            {
                setup.Bus.ForConsumersFromAssemblyContaining<DefaultDeliveryModeTests>(_ConfigureScannedConsumer);
                setup.Bus.ForMessage<ScannedMessage>(message => message.WithDeliveryMode(DeliveryMode.Direct));
            }
        );

        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IBus>().PublishAsync(new ScannedMessage("scan"), AbortToken);

        await _AssertStoredCountAsync(provider, MessageLane.Bus, 0);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    public void should_reject_an_undefined_type_policy_at_registration(int mode)
    {
        var act = () =>
            new ServiceCollection().AddHeadlessMessaging(setup =>
                setup.Bus.ForMessage<TestMessage>(message => message.WithDeliveryMode((DeliveryMode)mode))
            );

        act.Should().Throw<ArgumentException>().WithMessage("*mode*");
    }

    private static void _ConfigureScannedConsumer(ScannedConsumerContext context, IScannedConsumerBuilder builder)
    {
        if (context.MessageType != typeof(ScannedMessage))
        {
            builder.Skip();
            return;
        }

        builder.Contract("test.scanned").ConsumerIdentity($"tests.default-mode.{context.ConsumerType.Name}");
    }

    private static async Task _PublishAsync(
        IServiceProvider provider,
        MessageLane lane,
        TestMessage message,
        DeliveryMode? explicitMode
    )
    {
        await using var scope = provider.CreateAsyncScope();

        if (lane == MessageLane.Bus)
        {
            await scope
                .ServiceProvider.GetRequiredService<IBus>()
                .PublishAsync(message, new PublishOptions { DeliveryMode = explicitMode }, AbortToken);
        }
        else
        {
            await scope
                .ServiceProvider.GetRequiredService<IQueue>()
                .EnqueueAsync(message, new QueueOptions { DeliveryMode = explicitMode }, AbortToken);
        }
    }

    private static ServiceProvider _CreateProvider(
        DeliveryMode mode,
        Action<MessagingSetupBuilder>? registrations = null
    )
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessMessaging(setup =>
        {
            setup.UseInMemory();
            setup.UseInMemoryStorage();
            setup.Options.RequiredInboxCapability = MessagingInboxCapabilityTier.ProcessLocal;
            setup.Options.DefaultDeliveryMode = mode;
            if (registrations is null)
            {
                setup.Bus.ForMessage<TestMessage>(message => message.Contract("test.default-mode"));
                setup.Queue.ForMessage<TestMessage>(message => message.Contract("test.default-mode"));
            }
            else
            {
                registrations(setup);
            }
        });
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private static async Task _AssertStoredCountAsync(IServiceProvider provider, MessageLane lane, int expected)
    {
        var messages = await provider
            .GetRequiredService<IDataStorage>()
            .GetMonitoringApi()
            .GetMessagesAsync(
                new MessageQuery
                {
                    MessageType = MessageType.Publish,
                    Lane = lane,
                    PageSize = 10,
                },
                AbortToken
            );
        messages.TotalItems.Should().Be(expected);
    }

    private sealed record TestMessage(string Value);

    private sealed record ScannedMessage(string Value);

    private sealed class FirstScannedConsumer : IConsume<ScannedMessage>
    {
        public ValueTask ConsumeAsync(ConsumeContext<ScannedMessage> context, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }

    private sealed class SecondScannedConsumer : IConsume<ScannedMessage>
    {
        public ValueTask ConsumeAsync(ConsumeContext<ScannedMessage> context, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }
}
