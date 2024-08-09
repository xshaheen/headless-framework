using Microsoft.Extensions.DependencyInjection;

namespace Framework.Messaging.Cap;

public static class AddCapDistributedMessagingExtensions
{
    public static void AddCapDistributedMessaging(this IServiceCollection services)
    {
        services.AddSingleton<IDistributedMessagePublisher, CapDistributedMessagePublisher>();
        services.AddSingleton(CapDistributedMessageHandlerFactory.Create());
    }
}
