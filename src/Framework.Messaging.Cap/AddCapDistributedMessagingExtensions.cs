using DotNetCore.CAP;
using Framework.BuildingBlocks.Constants;
using Microsoft.Extensions.DependencyInjection;

namespace Framework.Messaging.Cap;

public static class AddCapDistributedMessagingExtensions
{
    public static void AddCapDistributedMessaging(this IServiceCollection services, Action<CapOptions> setupAction)
    {
        services.AddSingleton<IDistributedMessagePublisher, CapDistributedMessagePublisher>();
        services.AddSingleton(CapDistributedMessageHandlerFactory.Create());

        services.AddCap(capOptions =>
        {
            PlatformJsonConstants.ConfigureInternalJsonOptions(capOptions.JsonSerializerOptions);

            setupAction.Invoke(capOptions);
        });
    }
}
