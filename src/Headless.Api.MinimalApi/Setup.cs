// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api.Options;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Api;

[PublicAPI]
public static class SetupMinimalApi
{
    /// <summary>
    /// Registers Headless Minimal API defaults on the service collection: Headless JSON serialization
    /// options for <c>Microsoft.AspNetCore.Http.Json.JsonOptions</c> (camel-case, enum strings, etc.)
    /// with indented output enabled in Development and Test environments.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <returns><paramref name="services"/> for chaining.</returns>
    public static IServiceCollection ConfigureMinimalApi(this IServiceCollection services)
    {
        services.ConfigureOptions<ConfigureMinimalApiJsonOptions>();

        return services;
    }

    /// <summary>
    /// Registers Headless Minimal API defaults on <paramref name="builder"/>: Headless JSON serialization
    /// options for <c>Microsoft.AspNetCore.Http.Json.JsonOptions</c> (camel-case, enum strings, etc.)
    /// with indented output enabled in Development and Test environments.
    /// </summary>
    /// <param name="builder">The <see cref="WebApplicationBuilder"/> to configure.</param>
    public static void ConfigureMinimalApi(this WebApplicationBuilder builder)
    {
        builder.Services.ConfigureOptions<ConfigureMinimalApiJsonOptions>();
    }
}
