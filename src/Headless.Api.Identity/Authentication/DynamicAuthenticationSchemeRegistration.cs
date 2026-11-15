// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.Api.Identity;

internal static class DynamicAuthenticationSchemeRegistration
{
    /// <summary>
    /// Replaces the stock <see cref="AuthenticationSchemeProvider"/> with <see cref="DynamicAuthenticationSchemeProvider"/>,
    /// and leaves a scheme provider the application registered itself in place.
    /// </summary>
    public static void UseDynamicAuthenticationSchemeProvider(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();

        // The last registration is the one the container resolves. Replacing it unconditionally would silently drop a
        // provider the application registered before this call, while one registered after it would still win, so the
        // outcome would depend on call order. Only the stock provider (or none) is replaced.
        var current = services.LastOrDefault(d => d.ServiceType == typeof(IAuthenticationSchemeProvider));

        if (current is not null && current.ImplementationType != typeof(AuthenticationSchemeProvider))
        {
            return;
        }

        services.RemoveAll<IAuthenticationSchemeProvider>();
        services.AddSingleton<IAuthenticationSchemeProvider, DynamicAuthenticationSchemeProvider>();
    }
}
