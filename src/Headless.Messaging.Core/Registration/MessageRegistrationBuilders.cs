// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Headless.Checks;
using Headless.Messaging.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.Messaging.Registration;

/// <summary>Configures broadcast Bus message registrations.</summary>
[PublicAPI]
public interface IBusRegistrationBuilder
{
    /// <summary>Registers metadata and zero or more Bus consumers for a message type.</summary>
    IBusRegistrationBuilder ForMessage<TMessage>(Action<IBusMessageBuilder<TMessage>> configure)
        where TMessage : class;

    /// <summary>Scans an assembly for Bus consumers.</summary>
    IBusRegistrationBuilder ForConsumersFromAssembly(Assembly assembly);

    /// <summary>Scans an assembly for Bus consumers and configures each discovered registration.</summary>
    IBusRegistrationBuilder ForConsumersFromAssembly(
        Assembly assembly,
        [InstantHandle] Action<ScannedConsumerContext, IScannedConsumerBuilder> configure
    );

    /// <summary>Scans the assembly containing a marker type for Bus consumers.</summary>
    IBusRegistrationBuilder ForConsumersFromAssemblyContaining<TMarker>();

    /// <summary>Scans the assembly containing a marker type and configures its Bus consumers.</summary>
    IBusRegistrationBuilder ForConsumersFromAssemblyContaining<TMarker>(
        [InstantHandle] Action<ScannedConsumerContext, IScannedConsumerBuilder> configure
    );
}

/// <summary>Configures point-to-point Queue message registrations.</summary>
[PublicAPI]
public interface IQueueRegistrationBuilder
{
    /// <summary>Registers metadata and zero or more Queue consumers for a message type.</summary>
    IQueueRegistrationBuilder ForMessage<TMessage>(Action<IQueueMessageBuilder<TMessage>> configure)
        where TMessage : class;

    /// <summary>Scans an assembly for Queue consumers.</summary>
    IQueueRegistrationBuilder ForConsumersFromAssembly(Assembly assembly);

    /// <summary>Scans an assembly for Queue consumers and configures each discovered registration.</summary>
    IQueueRegistrationBuilder ForConsumersFromAssembly(
        Assembly assembly,
        [InstantHandle] Action<ScannedConsumerContext, IScannedConsumerBuilder> configure
    );

    /// <summary>Scans the assembly containing a marker type for Queue consumers.</summary>
    IQueueRegistrationBuilder ForConsumersFromAssemblyContaining<TMarker>();

    /// <summary>Scans the assembly containing a marker type and configures its Queue consumers.</summary>
    IQueueRegistrationBuilder ForConsumersFromAssemblyContaining<TMarker>(
        [InstantHandle] Action<ScannedConsumerContext, IScannedConsumerBuilder> configure
    );
}

internal abstract class MessageLaneRegistrationBuilder(MessagingSetupBuilder setup, MessageLane lane)
{
    protected MessagingSetupBuilder Setup { get; } = setup;

    protected void ScanAssembly(
        Assembly assembly,
        [InstantHandle] Action<ScannedConsumerContext, IScannedConsumerBuilder>? configure
    )
    {
        Argument.IsNotNull(assembly);

        foreach (var (consumerType, messageType) in _FindConsumers(assembly))
        {
            var builder = new ScannedConsumerBuilder(consumerType, lane);
            configure?.Invoke(new ScannedConsumerContext(consumerType, messageType), builder);

            if (builder.IsSkipped)
            {
                continue;
            }

            Setup.Services.TryAdd(new ServiceDescriptor(consumerType, consumerType, ServiceLifetime.Scoped));
            var serviceType = typeof(IConsume<>).MakeGenericType(messageType);
            Setup.Services.TryAdd(
                new ServiceDescriptor(serviceType, sp => sp.GetRequiredService(consumerType), ServiceLifetime.Scoped)
            );
            Setup.RegisterMessageRegistration(
                new MessageRegistration(
                    messageType,
                    lane,
                    MessageName: null,
                    CorrelationSelector: null,
                    ProviderConfigs: new Dictionary<Type, object>(),
                    Consumers: [builder.Build()]
                )
            );
        }
    }

    private static IEnumerable<(Type ConsumerType, Type MessageType)> _FindConsumers(Assembly assembly) =>
        assembly
            .GetTypes()
            .Where(static type => type is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false })
            .SelectMany(static consumerType =>
                consumerType
                    .GetInterfaces()
                    .Where(static type => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IConsume<>))
                    .Select(type => (ConsumerType: consumerType, MessageType: type.GetGenericArguments()[0]))
            );
}

internal sealed class BusRegistrationBuilder(MessagingSetupBuilder setup)
    : MessageLaneRegistrationBuilder(setup, MessageLane.Bus),
        IBusRegistrationBuilder
{
    public IBusRegistrationBuilder ForMessage<TMessage>(Action<IBusMessageBuilder<TMessage>> configure)
        where TMessage : class
    {
        Argument.IsNotNull(configure);
        var builder = new BusMessageBuilder<TMessage>(Setup.Services);
        configure(builder);
        Setup.RegisterMessageRegistration(builder.Build());
        return this;
    }

    public IBusRegistrationBuilder ForConsumersFromAssembly(Assembly assembly)
    {
        ScanAssembly(assembly, configure: null);
        return this;
    }

    public IBusRegistrationBuilder ForConsumersFromAssembly(
        Assembly assembly,
        Action<ScannedConsumerContext, IScannedConsumerBuilder> configure
    )
    {
        Argument.IsNotNull(configure);
        ScanAssembly(assembly, configure);
        return this;
    }

    public IBusRegistrationBuilder ForConsumersFromAssemblyContaining<TMarker>() =>
        ForConsumersFromAssembly(typeof(TMarker).Assembly);

    public IBusRegistrationBuilder ForConsumersFromAssemblyContaining<TMarker>(
        Action<ScannedConsumerContext, IScannedConsumerBuilder> configure
    ) => ForConsumersFromAssembly(typeof(TMarker).Assembly, configure);
}

internal sealed class QueueRegistrationBuilder(MessagingSetupBuilder setup)
    : MessageLaneRegistrationBuilder(setup, MessageLane.Queue),
        IQueueRegistrationBuilder
{
    public IQueueRegistrationBuilder ForMessage<TMessage>(Action<IQueueMessageBuilder<TMessage>> configure)
        where TMessage : class
    {
        Argument.IsNotNull(configure);
        var builder = new QueueMessageBuilder<TMessage>(Setup.Services);
        configure(builder);
        Setup.RegisterMessageRegistration(builder.Build());
        return this;
    }

    public IQueueRegistrationBuilder ForConsumersFromAssembly(Assembly assembly)
    {
        ScanAssembly(assembly, configure: null);
        return this;
    }

    public IQueueRegistrationBuilder ForConsumersFromAssembly(
        Assembly assembly,
        Action<ScannedConsumerContext, IScannedConsumerBuilder> configure
    )
    {
        Argument.IsNotNull(configure);
        ScanAssembly(assembly, configure);
        return this;
    }

    public IQueueRegistrationBuilder ForConsumersFromAssemblyContaining<TMarker>() =>
        ForConsumersFromAssembly(typeof(TMarker).Assembly);

    public IQueueRegistrationBuilder ForConsumersFromAssemblyContaining<TMarker>(
        Action<ScannedConsumerContext, IScannedConsumerBuilder> configure
    ) => ForConsumersFromAssembly(typeof(TMarker).Assembly, configure);
}

internal sealed record FrameworkConsumerRegistrationContribution(
    MessageLane Lane,
    Type MessageType,
    Type ConsumerType,
    string? MessageName,
    string? Group,
    byte Concurrency
);

internal static class FrameworkConsumerRegistrationExtensions
{
    public static void AddFrameworkConsumerRegistration<TMessage, TConsumer>(
        this IServiceCollection services,
        MessageLane lane,
        string? messageName = null,
        string? group = null,
        byte concurrency = 1
    )
        where TMessage : class
        where TConsumer : class, IConsume<TMessage>
    {
        if (
            services.Any(descriptor =>
                descriptor.ServiceType == typeof(FrameworkConsumerRegistrationContribution)
                && descriptor.ImplementationInstance is FrameworkConsumerRegistrationContribution contribution
                && contribution.Lane == lane
                && contribution.MessageType == typeof(TMessage)
                && contribution.ConsumerType == typeof(TConsumer)
            )
        )
        {
            return;
        }

        services.TryAddScoped<TConsumer>();
        services.TryAddScoped<IConsume<TMessage>>(sp => sp.GetRequiredService<TConsumer>());
        services.AddSingleton(
            new FrameworkConsumerRegistrationContribution(
                lane,
                typeof(TMessage),
                typeof(TConsumer),
                messageName,
                group,
                concurrency
            )
        );
    }
}
