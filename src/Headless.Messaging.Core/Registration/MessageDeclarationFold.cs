// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Configuration;

namespace Headless.Messaging.Registration;

/// <summary>One <c>WithMessageNameMapping&lt;T&gt;(name)</c> call: a raw name for the type on both lanes.</summary>
/// <param name="MessageType">The message type.</param>
/// <param name="Name">The raw logical message name.</param>
internal sealed record MessageNameMappingDeclaration(Type MessageType, string Name) : MessageDeclaration(MessageType);

/// <summary>The message declarations of one host, folded into the contracts and names they establish.</summary>
/// <param name="Contracts">One contract per declared message type, in first-declaration order.</param>
/// <param name="Routes">The route each contract contributes to each lane.</param>
/// <param name="NameMappings">The raw name mappings, in declaration order.</param>
internal sealed record MessageDeclarationFold(
    IReadOnlyList<MessageContract> Contracts,
    IReadOnlyList<MessageRegistration> Routes,
    IReadOnlyList<MessageNameMappingDeclaration> NameMappings
)
{
    /// <summary>
    /// Folds every declaration in registration order. The first contract for a message type contributes that type's
    /// route on both lanes; a later identical declaration, typically from a second module that shares the contracts
    /// package, merges into it, and a different one fails naming both.
    /// </summary>
    /// <exception cref="ArgumentException">An <c>AddMessageContract</c> name or version is not valid.</exception>
    /// <exception cref="InvalidOperationException">Two contract declarations of one message type differ.</exception>
    public static MessageDeclarationFold Create(IEnumerable<MessageDeclaration> declarations)
    {
        var contracts = new List<MessageContract>();
        var nameMappings = new List<MessageNameMappingDeclaration>();

        foreach (var declaration in declarations)
        {
            switch (declaration)
            {
                case MessageContract contract:
                    _AddContract(contracts, contract);
                    break;
                case MessageContractDeclaration contractDeclaration:
                    _AddContract(contracts, _ToContract(contractDeclaration));
                    break;
                case MessageNameMappingDeclaration mapping:
                    nameMappings.Add(mapping);
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Unknown message declaration {declaration.GetType().FullName ?? declaration.GetType().Name}."
                    );
            }
        }

        // The contract belongs to the message schema, not to a lane, so it names the message on both lanes. Startup
        // validates only the lanes the host's transport carries.
        var routes = contracts
            .SelectMany(static contract =>
                (IEnumerable<MessageRegistration>)
                    [contract.ToRegistration(MessageLane.Bus), contract.ToRegistration(MessageLane.Queue)]
            )
            .ToArray();

        return new MessageDeclarationFold(contracts, routes, nameMappings);
    }

    /// <summary>
    /// Reads one <c>AddMessageContract&lt;T&gt;(name, version)</c> declaration exactly as
    /// <c>Message&lt;T&gt;(name, version)</c> with no further settings, so it merges with and conflicts against the
    /// <c>ConfigureMessaging</c> declarations of the same type under the same rules.
    /// </summary>
    private static MessageContract _ToContract(MessageContractDeclaration declaration)
    {
        MessagingOptions.ValidateMessageName(declaration.Name);

        var noSettings = new MessageContractLaneSettings(
            RequiresRoutingAffinity: false,
            DeliveryMode: null,
            new ProviderConfigBag().Build()
        );

        return new MessageContract(
            declaration.MessageType,
            declaration.Name,
            MessagingOptions.ValidateContractVersion(declaration.Version),
            DeclaredCorrelationSelector: null,
            CorrelationSelector: null,
            noSettings,
            noSettings
        );
    }

    private static void _AddContract(List<MessageContract> contracts, MessageContract contract)
    {
        var existing = contracts.Find(existing => existing.MessageType == contract.MessageType);

        if (existing is null)
        {
            contracts.Add(contract);
            return;
        }

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
}
