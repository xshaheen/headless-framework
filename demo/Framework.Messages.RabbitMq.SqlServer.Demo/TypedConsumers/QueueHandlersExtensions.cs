using System.Reflection;

namespace Demo.TypedConsumers;

internal static class QueueHandlersExtensions
{
    private static readonly Type _QueueHandlerType = typeof(QueueHandler);

    public static IServiceCollection AddQueueHandlers(this IServiceCollection services, params Assembly[]? assemblies)
    {
        assemblies ??= [Assembly.GetEntryAssembly()!];

        foreach (var type in assemblies.Distinct().SelectMany(x => x.GetTypes().Where(_FilterHandlers)))
        {
            services.AddTransient(_QueueHandlerType, type);
        }

        return services;
    }

    private static bool _FilterHandlers(Type t)
    {
        var topic = t.GetCustomAttribute<QueueHandlerTopicAttribute>();

        return _QueueHandlerType.IsAssignableFrom(t) && topic != null && t is { IsClass: true, IsAbstract: false };
    }
}
