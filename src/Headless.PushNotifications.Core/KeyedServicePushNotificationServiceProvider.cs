// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.PushNotifications;

/// <summary>
/// <see cref="IPushNotificationServiceProvider"/> over the container's keyed <see cref="IPushNotificationService"/>
/// registrations — resolves the named instances added through <c>setup.AddNamed(name, …)</c>.
/// </summary>
internal sealed class KeyedServicePushNotificationServiceProvider(
    IServiceProvider serviceProvider,
    IReadOnlySet<string> registeredNames
) : IPushNotificationServiceProvider
{
    public IReadOnlySet<string> RegisteredNames { get; } = registeredNames;

    public IPushNotificationService GetService(string name)
    {
        Argument.IsNotNullOrWhiteSpace(name);

        return serviceProvider.GetKeyedService<IPushNotificationService>(name)
            ?? throw new InvalidOperationException(
                $"No push-notification service is registered under the name '{name}'. Register a named instance "
                    + $"first — for example setup.AddNamed(\"{name}\", i => i.UseFirebase(…)) or i.UseNoop()."
            );
    }

    public IPushNotificationService? GetServiceOrNull(string name)
    {
        Argument.IsNotNullOrWhiteSpace(name);

        return serviceProvider.GetKeyedService<IPushNotificationService>(name);
    }
}
