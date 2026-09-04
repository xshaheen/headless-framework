// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api.Options;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.Api;

[PublicAPI]
public static class SetupMvc
{
    /// <summary>
    /// Registers Headless MVC defaults on the service collection:
    /// <list type="bullet">
    ///   <item>Headless JSON serialization options for <c>Microsoft.AspNetCore.Mvc.JsonOptions</c>
    ///         (camel-case, enum strings, etc.) with indented output in Development and Test environments.</item>
    ///   <item>MVC behavior options: disables the automatic 400 model-state filter, enables
    ///         406 Not Acceptable for unsupported MIME types, and clears default model-validator
    ///         providers in favour of <c>SystemTextJsonValidationMetadataProvider</c>.</item>
    /// </list>
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <returns><paramref name="services"/> for chaining.</returns>
    public static IServiceCollection ConfigureMvc(this IServiceCollection services)
    {
        services.ConfigureOptions<ConfigureMvcJsonOptions>();
        services.ConfigureOptions<ConfigureMvcApiOptions>();

        return services;
    }

    /// <summary>
    /// Registers Headless MVC defaults on <paramref name="builder"/>: same JSON and API behavior
    /// options as <see cref="ConfigureMvc(IServiceCollection)"/>.
    /// </summary>
    /// <param name="builder">The <see cref="WebApplicationBuilder"/> to configure.</param>
    /// <returns><paramref name="builder"/> for chaining.</returns>
    public static WebApplicationBuilder ConfigureMvc(this WebApplicationBuilder builder)
    {
        builder.Services.ConfigureOptions<ConfigureMvcJsonOptions>();
        builder.Services.ConfigureOptions<ConfigureMvcApiOptions>();

        return builder;
    }

    /// <summary>
    /// Adds the opt-in MVC profile for strong ETag responses and <c>If-Match</c> preconditions.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <returns><paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddHeadlessEtagConcurrency(this IServiceCollection services)
    {
        services.TryAddScoped<IfMatchContext>();
        services.ConfigureOptions<ConfigureEtagConcurrencyMvcOptions>();

        return services;
    }
}
