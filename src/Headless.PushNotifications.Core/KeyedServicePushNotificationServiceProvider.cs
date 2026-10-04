// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.PushNotifications;

/// <summary>
/// Resolves named <see cref="IPushNotificationService"/> instances from keyed container registrations.
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
                    + $"first — for example setup.AddNamed(\"{name}\", i => i.UseApns(…)), i.UseFirebase(…), or i.UseNoop()."
            );
    }

    public IPushNotificationService? GetServiceOrNull(string name)
    {
        Argument.IsNotNullOrWhiteSpace(name);

        return serviceProvider.GetKeyedService<IPushNotificationService>(name);
    }
}
