// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Registration;

internal static class MessageContractRegistration
{
    /// <summary>The route registration a contract contributes to one lane.</summary>
    public static MessageRegistration ToRegistration(this MessageContract contract, MessageLane lane)
    {
        var settings = lane == MessageLane.Bus ? contract.Bus : contract.Queue;

        return new MessageRegistration(
            contract.MessageType,
            lane,
            contract.Name,
            contract.CorrelationSelector,
            settings.ProviderConfigs,
            Consumers: [],
            ContractVersion: contract.Version,
            RequiresRoutingAffinity: settings.RequiresRoutingAffinity,
            DeliveryMode: settings.DeliveryMode
        );
    }
}
