// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.DependencyInjection;

namespace Headless.Messaging;

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
    private readonly List<MessageContractBuilder> _contracts = [];

    internal MessagingContributionBuilder(IServiceCollection services)
    {
        Services = services;
    }

    /// <summary>The service collection this contribution records into, for Core's consumer tuning.</summary>
    internal IServiceCollection Services { get; }

    /// <summary>
    /// Declares the contract of one message type for both lanes: the logical name publishing and consuming resolve the
    /// type to, and its schema version. Chain <see cref="IMessageContractBuilder{TMessage}.CorrelateBy"/>,
    /// <see cref="IMessageContractBuilder{TMessage}.OnBus"/>, and <see cref="IMessageContractBuilder{TMessage}.OnQueue"/>
    /// for the remaining settings.
    /// </summary>
    /// <remarks>
    /// The message type needs no attribute and no reference to Headless, so a contracts package stays framework-free.
    /// Several modules may declare the same contract: identical declarations merge, and declarations that differ in name,
    /// version, correlation selector, or lane settings fail naming both when messaging builds the host's consumer
    /// registry. The declaration is recorded when the <c>ConfigureMessaging</c> callback returns, so configure it inside
    /// that callback.
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
        Services.AddMessagingModuleContribution<TModule>();
        return this;
    }

    /// <summary>
    /// Records every contract this contribution declared, in declaration order. Messaging merges and checks them
    /// against every other declaration when the host's consumer registry freezes.
    /// </summary>
    internal void Complete()
    {
        foreach (var contract in _contracts)
        {
            Services.AddSingleton<MessageDeclaration>(contract.Complete());
        }

        _contracts.Clear();
    }
}
