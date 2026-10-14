// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.DependencyInjection;

namespace Headless.Messaging.Internal;

/// <summary>Resolves the middleware instances that registry descriptors name, from one service provider.</summary>
internal static class MiddlewareDescriptorResolver
{
    /// <summary>
    /// Returns, in descriptor order, the first service registered under each descriptor's service type whose runtime
    /// type is exactly the descriptor's middleware type, skipping a descriptor that has none.
    /// </summary>
    /// <remarks>
    /// Descriptors often share one service type (every receive middleware does), so each distinct service type is
    /// resolved once per call rather than once per descriptor. Within one provider, repeated resolution returns the
    /// same scoped or singleton instances in the same order, so the instance matched is the one a fresh resolution
    /// per descriptor would match.
    /// </remarks>
    public static List<object> Resolve(IServiceProvider provider, IReadOnlyList<MiddlewareDescriptor> descriptors)
    {
        var resolved = new List<object>(descriptors.Count);
        var servicesByType = new (Type ServiceType, object?[] Services)[descriptors.Count];
        var distinctTypes = 0;

        for (var i = 0; i < descriptors.Count; i++)
        {
            var descriptor = descriptors[i];
            object?[]? services = null;

            for (var j = 0; j < distinctTypes; j++)
            {
                if (servicesByType[j].ServiceType == descriptor.ServiceType)
                {
                    services = servicesByType[j].Services;
                    break;
                }
            }

            if (services is null)
            {
                var all = provider.GetServices(descriptor.ServiceType);
                services = all as object?[] ?? [.. all];
                servicesByType[distinctTypes++] = (descriptor.ServiceType, services);
            }

            foreach (var service in services)
            {
                if (service?.GetType() == descriptor.MiddlewareType)
                {
                    resolved.Add(service);
                    break;
                }
            }
        }

        return resolved;
    }
}
