// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api.Options;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Api;

[PublicAPI]
public static class SetupMvc
{
    /// <summary>
    /// Registers Headless MVC defaults on the service collection:
    /// <list type="bullet">
    ///   <item>Headless JSON serialization options for <c>Microsoft.AspNetCore.Mvc.JsonOptions</c>
    ///         (camel-case, enum strings, etc.) with indented output in Development and Test environments.</item>
    ///   <item>MVC behavior options: enables 406 Not Acceptable for unsupported MIME types, clears the
    ///         default model-validator providers, and shapes the automatic <c>[ApiController]</c> 400 for a value
    ///         that cannot be bound.</item>
    /// </list>
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <returns><paramref name="services"/> for chaining.</returns>
    public static IServiceCollection ConfigureHeadlessMvc(this IServiceCollection services)
    {
        services.ConfigureOptions<ConfigureMvcJsonOptions>();
        services.ConfigureOptions<ConfigureMvcApiOptions>();

        return services;
    }

    /// <summary>
    /// Registers Headless MVC defaults on <paramref name="builder"/>: same JSON and API behavior
    /// options as <see cref="ConfigureHeadlessMvc(IServiceCollection)"/>.
    /// </summary>
    /// <param name="builder">The <see cref="WebApplicationBuilder"/> to configure.</param>
    /// <returns><paramref name="builder"/> for chaining.</returns>
    public static WebApplicationBuilder ConfigureHeadlessMvc(this WebApplicationBuilder builder)
    {
        builder.Services.ConfigureOptions<ConfigureMvcJsonOptions>();
        builder.Services.ConfigureOptions<ConfigureMvcApiOptions>();

        return builder;
    }

    /// <summary>
    /// Adds the opt-in MVC profile that validates every action argument whose type has a registered FluentValidation
    /// <c>IValidator&lt;T&gt;</c> before the action runs, answering one 422 with the failures of all of them, as
    /// <c>ValidateArguments()</c> does for a Minimal API endpoint.
    /// </summary>
    /// <remarks>
    /// Leave it off when the actions send their arguments through a mediator that validates them, such as one with
    /// <c>AddMediatorValidationRequestBehavior()</c>: both would run the same validators on every request. Registration is
    /// idempotent.
    /// </remarks>
    /// <param name="services">The service collection to configure.</param>
    /// <returns><paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddHeadlessMvcArgumentValidation(this IServiceCollection services)
    {
        services.ConfigureOptions<ConfigureArgumentValidationMvcOptions>();

        return services;
    }

    /// <summary>
    /// Adds the opt-in MVC profile for strong ETag responses and <c>If-Match</c> preconditions.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <returns><paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddHeadlessMvcEntityTagConcurrency(this IServiceCollection services)
    {
        services.AddHeadlessEntityTagConcurrencyCore();
        services.ConfigureOptions<ConfigureEntityTagConcurrencyMvcOptions>();

        return services;
    }

    /// <summary>
    /// Adds the opt-in MVC profile for strong ETag responses and <c>If-Match</c> preconditions with
    /// representation-specific validation.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="configure">Configures entity-tag concurrency validation.</param>
    /// <returns><paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddHeadlessMvcEntityTagConcurrency(
        this IServiceCollection services,
        Action<EntityTagConcurrencyOptions> configure
    )
    {
        services.AddHeadlessMvcEntityTagConcurrency();
        services.Configure(configure);

        return services;
    }
}
