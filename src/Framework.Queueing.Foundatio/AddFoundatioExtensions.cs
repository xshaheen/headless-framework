using Microsoft.Extensions.DependencyInjection;

namespace Framework.Messaging;

[PublicAPI]
public static class AddFoundatioExtensions
{
    public static IServiceCollection AddFoundatioQueue(this IServiceCollection services)
    {
        // services.AddSingleton<IMessageBus, FoundatioMessageBus>();
        // services.AddSingleton<IMessagePublisher>(provider => provider.GetRequiredService<IMessageBus>());
        // services.AddSingleton<IMessageSubscriber>(provider => provider.GetRequiredService<IMessageBus>());

        return services;
    }
}
