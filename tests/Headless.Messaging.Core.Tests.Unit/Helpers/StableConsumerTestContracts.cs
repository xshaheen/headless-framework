// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Registration;

namespace Tests.Helpers;

internal static class StableConsumerTestContracts
{
    public static void UseProcessLocalInMemoryStorage(this MessagingSetupBuilder setup)
    {
        setup.Options.RequiredInboxCapability = MessagingInboxCapabilityTier.ProcessLocal;
        setup.UseInMemoryStorage();
    }

    public static IBusConsumerBuilder<TConsumer> StableContract<TConsumer>(
        this IBusConsumerBuilder<TConsumer> builder,
        string identity
    )
        where TConsumer : class
    {
        return builder.ConsumerIdentity(identity).ContractVersion("v1");
    }

    public static IQueueConsumerBuilder<TConsumer> StableContract<TConsumer>(
        this IQueueConsumerBuilder<TConsumer> builder,
        string identity
    )
        where TConsumer : class
    {
        return builder.ConsumerIdentity(identity).ContractVersion("v1");
    }

    public static IScannedConsumerBuilder StableContract(this IScannedConsumerBuilder builder, string identity)
    {
        return builder.ConsumerIdentity(identity).ContractVersion("v1");
    }

    public static void ConfigureKnownScannedConsumer(ScannedConsumerContext context, IScannedConsumerBuilder builder)
    {
        var identity = (
            context.ConsumerType.DeclaringType?.Name,
            context.ConsumerType.Name,
            context.MessageType.Name
        ) switch
        {
            ("ForMessageRegistrationTests", "OrderPlacedHandler", "OrderPlaced") => "tests.registration.orders-primary",
            ("ForMessageRegistrationTests", "OrderPlacedAnalyticsHandler", "OrderPlaced") =>
                "tests.registration.orders-analytics",
            ("ForMessageRegistrationTests", "OtherOrderPlacedHandler", "OtherOrderPlaced") =>
                "tests.registration.other-orders-primary",
            (null, "OrderPlacedConsumer", "OrderPlaced") => "tests.integration.orders-primary",
            (null, "OrderAnalyticsConsumer", "OrderPlaced") => "tests.integration.orders-analytics",
            (null, "OrderCancelledConsumer", "OrderCancelled") => "tests.integration.orders-cancelled",
            (null, "MultiEventConsumer", "OrderPlaced") => "tests.integration.multi-event-placed",
            (null, "MultiEventConsumer", "OrderCancelled") => "tests.integration.multi-event-cancelled",
            _ => null,
        };

        if (identity is null)
        {
            builder.Skip();
            return;
        }

        builder.StableContract(identity);
    }
}
