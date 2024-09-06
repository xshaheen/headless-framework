using DotNetCore.CAP;
using Framework.Kernel.BuildingBlocks.Constants;
using Framework.Messaging.Cap.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace Framework.Messaging.Cap;

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
            PlatformJsonConstants.ConfigureInternalJsonOptions(capOptions.JsonSerializerOptions);
            setupAction.Invoke(capOptions);
        });

        capBuilder.AddSubscribeFilter<StopMarkHttpTimeoutAsSuccessFilter>();

        return capBuilder;
    }
}
