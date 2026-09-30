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
            new MessageRegistration(
                typeof(TMessage),
                lane,
                messageName,
                CorrelationSelector: null,
                ProviderConfigs: new Dictionary<Type, object>(),
                Consumers:
                [
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
                ],
                ContractVersion: messageContractVersion,
                DeclaresMessage: false
            )
        );

        return this;
    }
}
