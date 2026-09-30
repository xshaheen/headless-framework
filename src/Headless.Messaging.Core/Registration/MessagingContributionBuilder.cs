// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.Messaging.Registration;

/// <summary>
/// Collects one module's Messaging registrations for <c>services.ConfigureMessaging(...)</c>. Every registration is
/// recorded as an immutable descriptor in the service collection and applied when messaging starts, so a module can
/// contribute before or after the host calls <c>AddHeadlessMessaging</c>.
/// </summary>
/// <remarks>
/// A contribution never configures the Messaging runtime itself: transports, storage, and host options stay with the
/// single <c>AddHeadlessMessaging</c> call. In a host that never calls it, contributions stay inert.
/// </remarks>
[PublicAPI]
public sealed class MessagingContributionBuilder
{
    private readonly MessageRegistrationSink _sink;
    private readonly List<MessageContractBuilder> _contracts = [];

    internal MessagingContributionBuilder(IServiceCollection services, ConsumerRegistry registry)
    {
        _sink = new MessageRegistrationSink(services, registry);
        Bus = new BusRegistrationBuilder(_sink);
        Queue = new QueueRegistrationBuilder(_sink);
    }

    /// <summary>Gets the registration root for Bus consumers.</summary>
    public IBusRegistrationBuilder Bus { get; }

    /// <summary>Gets the registration root for Queue consumers.</summary>
    public IQueueRegistrationBuilder Queue { get; }

    /// <summary>
    /// Declares the contract of one message type for both lanes: the logical name publishing and consuming resolve the
    /// type to, and its schema version. Chain <see cref="IMessageContractBuilder{TMessage}.CorrelateBy"/>,
    /// <see cref="IMessageContractBuilder{TMessage}.OnBus"/>, and <see cref="IMessageContractBuilder{TMessage}.OnQueue"/>
    /// for the remaining settings.
    /// </summary>
    /// <remarks>
    /// The message type needs no attribute and no reference to Headless, so a contracts package stays framework-free.
    /// Several modules may declare the same contract: identical declarations merge, and declarations that differ in name,
    /// version, correlation selector, or lane settings fail naming both. The declaration is recorded when the
    /// <c>ConfigureMessaging</c> callback returns, so configure it inside that callback.
    /// </remarks>
    /// <typeparam name="TMessage">The message type.</typeparam>
    /// <param name="name">The stable logical message name, for example <c>orders.placed</c>.</param>
    /// <param name="version">The contract schema version, compared ordinally.</param>
    /// <returns>A builder for the contract's remaining settings.</returns>
    /// <exception cref="ArgumentException"><paramref name="name"/> or <paramref name="version"/> is not valid.</exception>
    public IMessageContractBuilder<TMessage> Message<TMessage>(
        string name,
        string version = MessageOptions.InitialContractVersion
    )
        where TMessage : class
    {
        var contract = new MessageContractBuilder<TMessage>(name, version);
        _contracts.Add(contract);
        return contract;
    }

    /// <summary>
    /// Contributes one assembly's generated consumers, for example <c>AddModule&lt;Billing.MessagingModule&gt;()</c>.
    /// Contributing a module more than once, from here or from the <c>AddHeadlessMessaging</c> setup, is harmless: the
    /// host registers it once.
    /// </summary>
    /// <remarks>
    /// The Messaging source generator emits one <see cref="IMessagingModule"/> per assembly that declares
    /// <see cref="BusConsumerAttribute"/> or <see cref="QueueConsumerAttribute"/> consumers. Its consumers register when
    /// messaging starts, so the call may come before or after <c>AddHeadlessMessaging</c>.
    /// </remarks>
    /// <typeparam name="TModule">The generated <see cref="IMessagingModule"/> of the assembly.</typeparam>
    /// <returns>This builder, for chaining.</returns>
    public MessagingContributionBuilder AddModule<TModule>()
        where TModule : IMessagingModule
    {
        _sink.Services.AddMessagingModuleContribution<TModule>();
        return this;
    }

    /// <summary>
    /// Tunes the deployment settings of one declared consumer, for example
    /// <c>Tune("billing.invoice-projection", consumer =&gt; consumer.Concurrency(16))</c>.
    /// </summary>
    /// <remarks>
    /// The identity is checked when messaging starts: an identity no registered consumer declares fails startup.
    /// <paramref name="configure"/> runs once, synchronously, during this call.
    /// </remarks>
    /// <param name="identity">The consumer's identity.</param>
    /// <param name="configure">Changes the consumer's deployment settings.</param>
    /// <returns>This builder, for chaining.</returns>
    /// <exception cref="ArgumentException"><paramref name="identity"/> is empty or whitespace.</exception>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public MessagingContributionBuilder Tune(string identity, [InstantHandle] Action<ConsumerTuningBuilder> configure)
    {
        _sink.Services.AddConsumerTuning(identity, configure);
        return this;
    }

    /// <summary>Records every contract this contribution declared, in declaration order.</summary>
    internal void Complete()
    {
        foreach (var contract in _contracts)
        {
            _sink.RegisterContract(contract.Complete());
        }

        _contracts.Clear();
    }

    /// <summary>
    /// Contributes one consumer without declaring the message's contract settings, so it can join a message that
    /// another registration declares. Contributing the same consumer twice with identical settings is harmless; any
    /// difference fails when messaging starts.
    /// </summary>
    internal MessagingContributionBuilder AddConsumerContribution<TMessage, TConsumer>(
        MessageLane lane,
        string consumerIdentity,
        string messageContractVersion,
        string? messageName = null,
        string? group = null,
        byte concurrency = 1
    )
        where TMessage : class
        where TConsumer : class, IConsume<TMessage>
    {
        Argument.IsNotNullOrWhiteSpace(consumerIdentity);
        MessagingOptions.ValidateContractVersion(messageContractVersion);

        _sink.Services.TryAddScoped<TConsumer>();
        _sink.Services.TryAddScoped<IConsume<TMessage>>(sp => sp.GetRequiredService<TConsumer>());
        _sink.Register(
            MessageRegistration.ConsumerOnly(
                typeof(TMessage),
                lane,
                messageName,
                new MessageConsumerRegistration(
                    typeof(TConsumer),
                    lane,
                    IsAssemblyScan: false,
                    group,
                    concurrency,
                    HandlerId: null,
                    ConsumerIdentity: consumerIdentity,
                    CircuitBreakerOverride: null,
                    ProviderConfigs: new Dictionary<Type, object>()
                ),
                messageContractVersion
            )
        );

        return this;
    }
}

/// <summary>One generated module that a contribution or the host asked messaging to register.</summary>
/// <param name="ModuleType">The generated module type, which identifies the module across contributions.</param>
/// <param name="Register">Runs the module's generated registration against a catalog.</param>
internal sealed record MessagingModuleContribution(Type ModuleType, Action<MessagingCatalogBuilder> Register);

internal static class MessagingModuleContributionRecording
{
    public static void AddMessagingModuleContribution<TModule>(this IServiceCollection services)
        where TModule : IMessagingModule
    {
        services.AddSingleton(
            new MessagingModuleContribution(typeof(TModule), static catalog => TModule.Register(catalog))
        );
    }
}
