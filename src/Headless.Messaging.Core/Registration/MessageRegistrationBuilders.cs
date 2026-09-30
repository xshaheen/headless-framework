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

    /// <summary>Scans an assembly for Bus consumers and configures each discovered registration.</summary>
    IBusRegistrationBuilder ForConsumersFromAssembly(
        Assembly assembly,
        [InstantHandle] Action<ScannedConsumerContext, IScannedConsumerBuilder> configure
    );

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

    /// <summary>Scans an assembly for Queue consumers and configures each discovered registration.</summary>
    IQueueRegistrationBuilder ForConsumersFromAssembly(
        Assembly assembly,
        [InstantHandle] Action<ScannedConsumerContext, IScannedConsumerBuilder> configure
    );

    /// <summary>Scans the assembly containing a marker type and configures its Queue consumers.</summary>
    IQueueRegistrationBuilder ForConsumersFromAssemblyContaining<TMarker>(
        [InstantHandle] Action<ScannedConsumerContext, IScannedConsumerBuilder> configure
    );
}

internal abstract class MessageLaneRegistrationBuilder(MessageRegistrationSink sink, MessageLane lane)
{
    protected MessageRegistrationSink Sink { get; } = sink;

    protected void ScanAssembly(
        Assembly assembly,
        [InstantHandle] Action<ScannedConsumerContext, IScannedConsumerBuilder> configure
    )
    {
        Argument.IsNotNull(assembly);

        foreach (var (consumerType, messageType) in _FindConsumers(assembly))
        {
            var builder = new ScannedConsumerBuilder(consumerType, lane);
            configure(new ScannedConsumerContext(consumerType, messageType), builder);

            if (builder.IsSkipped)
            {
                continue;
            }

            Sink.Services.TryAdd(new ServiceDescriptor(consumerType, consumerType, ServiceLifetime.Scoped));
            var serviceType = typeof(IConsume<>).MakeGenericType(messageType);
            Sink.Services.TryAdd(
                new ServiceDescriptor(serviceType, sp => sp.GetRequiredService(consumerType), ServiceLifetime.Scoped)
            );
            Sink.Register(
                MessageRegistration.ConsumerOnly(
                    messageType,
                    lane,
                    builder.MessageName,
                    builder.Build(),
                    builder.ContractVersion
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

internal sealed class BusRegistrationBuilder(MessageRegistrationSink sink)
    : MessageLaneRegistrationBuilder(sink, MessageLane.Bus),
        IBusRegistrationBuilder
{
    public IBusRegistrationBuilder ForMessage<TMessage>(Action<IBusMessageBuilder<TMessage>> configure)
        where TMessage : class
    {
        Argument.IsNotNull(configure);
        var builder = new BusMessageBuilder<TMessage>(Sink.Services);
        configure(builder);
        Sink.Register(builder.Build());
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

    public IBusRegistrationBuilder ForConsumersFromAssemblyContaining<TMarker>(
        Action<ScannedConsumerContext, IScannedConsumerBuilder> configure
    ) => ForConsumersFromAssembly(typeof(TMarker).Assembly, configure);
}

internal sealed class QueueRegistrationBuilder(MessageRegistrationSink sink)
    : MessageLaneRegistrationBuilder(sink, MessageLane.Queue),
        IQueueRegistrationBuilder
{
    public IQueueRegistrationBuilder ForMessage<TMessage>(Action<IQueueMessageBuilder<TMessage>> configure)
        where TMessage : class
    {
        Argument.IsNotNull(configure);
        var builder = new QueueMessageBuilder<TMessage>(Sink.Services);
        configure(builder);
        Sink.Register(builder.Build());
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

    public IQueueRegistrationBuilder ForConsumersFromAssemblyContaining<TMarker>(
        Action<ScannedConsumerContext, IScannedConsumerBuilder> configure
    ) => ForConsumersFromAssembly(typeof(TMarker).Assembly, configure);
}

/// <summary>
/// The one place message registrations enter a service collection, shared by the <c>AddHeadlessMessaging</c> setup
/// callback and every <c>ConfigureMessaging</c> contribution. Each registration is recorded as an immutable
/// <see cref="MessageRegistration"/> descriptor that bootstrap drains in registration order, so a contribution counts
/// whether it was added before or after <c>AddHeadlessMessaging</c>.
/// </summary>
internal sealed class MessageRegistrationSink(IServiceCollection services, ConsumerRegistry registry)
{
    public IServiceCollection Services { get; } = services;

    public ConsumerRegistry Registry { get; } = registry;

    public void Register(MessageRegistration registration)
    {
        Argument.IsNotNull(registration);

        // Message-level metadata (correlation, provider configuration, delivery mode) has one owner per lane, so two
        // declaring registrations would silently compete. Consumer-only registrations carry none and may join it.
        var duplicateDeclaration =
            registration.DeclaresMessage
            && Services.Any(descriptor =>
                descriptor.ServiceType == typeof(MessageRegistration)
                && descriptor.ImplementationInstance is MessageRegistration existing
                && existing.DeclaresMessage
                && existing.MessageType == registration.MessageType
                && existing.Lane == registration.Lane
            );
        if (duplicateDeclaration)
        {
            throw new InvalidOperationException(
                $"Message type {registration.MessageType.Name} is registered more than once on lane {registration.Lane}. "
                    + "Register each message type once per lane and configure all consumers in that registration, "
                    + "and do not also declare it with Message<T>(name, version)."
            );
        }

        Services.AddSingleton(registration);

        if (registration.MessageName is { } messageName)
        {
            Registry.RegisterMessageName(registration.MessageType, registration.Lane, messageName);
        }
    }

    /// <summary>
    /// Records one lane-agnostic message contract. The first declaration for a message type contributes that type's
    /// route on both lanes; a later identical declaration, typically from a second module that shares the contracts
    /// package, merges into it, and a different one fails naming both.
    /// </summary>
    public void RegisterContract(MessageContract contract)
    {
        Argument.IsNotNull(contract);

        var existing = Services
            .Select(static descriptor => descriptor.ImplementationInstance)
            .OfType<MessageContract>()
            .FirstOrDefault(existing => existing.MessageType == contract.MessageType);

        if (existing is not null)
        {
            if (existing.IsSameDeclarationAs(contract))
            {
                return;
            }

            throw new InvalidOperationException(
                $"Message type {contract.MessageType.FullName ?? contract.MessageType.Name} has conflicting contract "
                    + $"declarations: {existing.Describe()} and {contract.Describe()}. A message has one contract for "
                    + "both lanes; declare it once, or make every declaration identical."
            );
        }

        // A contract owns the type's message-level settings on both lanes, so a lane-owned ForMessage<T> declaration
        // for the same type would compete with it on that lane.
        var laneDeclaration = Services
            .Select(static descriptor => descriptor.ImplementationInstance)
            .OfType<MessageRegistration>()
            .FirstOrDefault(registration =>
                registration.DeclaresMessage && registration.MessageType == contract.MessageType
            );

        if (laneDeclaration is not null)
        {
            throw new InvalidOperationException(
                $"Message type {contract.MessageType.FullName ?? contract.MessageType.Name} is already declared on "
                    + $"lane {laneDeclaration.Lane} through ForMessage, so {contract.Describe()} cannot also declare it. "
                    + "Declare the message once."
            );
        }

        Services.AddSingleton(contract);
        Register(contract.ToRegistration(MessageLane.Bus));
        Register(contract.ToRegistration(MessageLane.Queue));
    }
}

internal static class FrameworkConsumerRegistrationExtensions
{
    /// <summary>
    /// Contributes one framework-owned consumer through the same deferred path as <c>ConfigureMessaging</c>, so it
    /// registers whether the host calls <c>AddHeadlessMessaging</c> before or after the owning package's setup.
    /// </summary>
    /// <remarks>
    /// <paramref name="everyInstance"/> makes a Bus consumer receive every message in every process, for a framework
    /// consumer that refreshes per-process state; it cannot be combined with <paramref name="group"/>.
    /// </remarks>
    public static void AddFrameworkConsumerRegistration<TMessage, TConsumer>(
        this IServiceCollection services,
        MessageLane lane,
        string consumerIdentity,
        string messageContractVersion,
        string? messageName = null,
        string? group = null,
        byte concurrency = 1,
        bool everyInstance = false
    )
        where TMessage : class
        where TConsumer : class, IConsume<TMessage>
    {
        services.ConfigureMessaging(messaging =>
            messaging.AddConsumerContribution<TMessage, TConsumer>(
                lane,
                consumerIdentity,
                messageContractVersion,
                messageName,
                group,
                concurrency,
                everyInstance
            )
        );
    }
}
