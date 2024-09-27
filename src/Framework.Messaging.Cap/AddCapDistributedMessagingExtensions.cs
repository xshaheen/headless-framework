// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using DotNetCore.CAP;
using Framework.Kernel.BuildingBlocks;
using Framework.Messaging.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace Framework.Messaging;

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
            capOptions.FailedMessageExpiredAfter = 30 * 24 * 3600; // 30 days
            capOptions.SucceedMessageExpiredAfter = 5 * 24 * 3600; // 30 days
            capOptions.CollectorCleaningInterval = 5 * 60; // 5 minutes
            FrameworkJsonConstants.ConfigureInternalJsonOptions(capOptions.JsonSerializerOptions);
            setupAction.Invoke(capOptions);
        });

        capBuilder.AddSubscribeFilter<StopMarkHttpTimeoutAsSuccessFilter>();

        return capBuilder;
    }
}
