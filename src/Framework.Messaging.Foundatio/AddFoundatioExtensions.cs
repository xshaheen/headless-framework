using Microsoft.Extensions.DependencyInjection;

namespace Framework.Messaging;

[PublicAPI]
public static class AddFoundatioExtensions
{
    public static IServiceCollection AddMessageBusFoundatioAdapter(this IServiceCollection services)
    {
        services.AddSingleton<IMessageBus, MessageBusFoundatioAdapter>();
        services.AddSingleton<IMessagePublisher>(provider => provider.GetRequiredService<IMessageBus>());
        services.AddSingleton<IMessageSubscriber>(provider => provider.GetRequiredService<IMessageBus>());

        return services;
    }
}
