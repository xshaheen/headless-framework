// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Messaging.Registration;

/// <summary>
/// The one place message contracts enter a service collection, shared by the <c>AddHeadlessMessaging</c> setup callback
/// and every <c>ConfigureMessaging</c> contribution. Each contract is recorded as immutable descriptors that bootstrap
/// drains in registration order, so a contribution counts whether it was added before or after
/// <c>AddHeadlessMessaging</c>.
/// </summary>
internal sealed class MessageRegistrationSink(IServiceCollection services, ConsumerRegistry registry)
{
    public IServiceCollection Services { get; } = services;

    /// <summary>
    /// Records one <c>AddMessageContract&lt;T&gt;(name, version)</c> declaration exactly as
    /// <c>Message&lt;T&gt;(name, version)</c> with no further settings, so it merges with and conflicts against the
    /// <c>ConfigureMessaging</c> declarations of the same type under the same rules.
    /// </summary>
    public void RegisterContract(MessageContractDeclaration declaration)
    {
        Argument.IsNotNull(declaration);
        MessagingOptions.ValidateMessageName(declaration.Name);

        var noSettings = new MessageContractLaneSettings(
            RequiresRoutingAffinity: false,
            DeliveryMode: null,
            new ProviderConfigBag().Build()
        );

        RegisterContract(
            new MessageContract(
                declaration.MessageType,
                declaration.Name,
                MessagingOptions.ValidateContractVersion(declaration.Version),
                DeclaredCorrelationSelector: null,
                CorrelationSelector: null,
                noSettings,
                noSettings
            )
        );
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

        Services.AddSingleton(contract);

        // The contract belongs to the message schema, not to a lane, so it names the message on both lanes. Startup
        // validates only the lanes the host's transport carries.
        foreach (var lane in (ReadOnlySpan<MessageLane>)[MessageLane.Bus, MessageLane.Queue])
        {
            var registration = contract.ToRegistration(lane);
            Services.AddSingleton(registration);
            registry.RegisterMessageName(registration.MessageType, lane, contract.Name);
        }
    }
}
