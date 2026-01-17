// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Domain.Filters;
using Framework.Messages.Configuration;
using Framework.Serializer;
using Microsoft.Extensions.DependencyInjection;

namespace Framework.Domain;

[PublicAPI]
public static class AddCapDistributedMessagingExtensions
{
    public static CapBuilder AddCapDistributedMessaging(
        this IServiceCollection services,
        Action<CapOptions> setupAction
    )
    {
        services.AddSingleton<IDistributedMessagePublisher, CapDistributedMessagePublisher>();
        services.AddSingleton(CapDistributedMessageHandlerFactory.Create());

        var capBuilder = services.AddCap(capOptions =>
        {
            JsonConstants.ConfigureInternalJsonOptions(capOptions.JsonSerializerOptions);
            capOptions.FailedMessageExpiredAfter = 30 * 24 * 3600; // 30 days
            capOptions.SucceedMessageExpiredAfter = 5 * 24 * 3600; // 30 days
            capOptions.CollectorCleaningInterval = 5 * 60; // 5 minutes
            setupAction.Invoke(capOptions);
        });

        capBuilder.AddSubscribeFilter<StopMarkHttpTimeoutAsSuccessConsumeFilter>();

        return capBuilder;
    }
}
