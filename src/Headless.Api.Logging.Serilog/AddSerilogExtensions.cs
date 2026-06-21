// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Logging;

[PublicAPI]
public static class AddSerilogExtensions
{
    /// <summary>
    /// Registers <see cref="SerilogEnrichersMiddleware"/> as a scoped service so that it can be resolved
    /// per-request when <see cref="UseSerilogEnrichers"/> adds it to the pipeline.
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    /// <returns><paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddSerilogEnrichers(this IServiceCollection services)
    {
        return services.AddScoped<SerilogEnrichersMiddleware>();
    }

    /// <summary>
    /// Adds <see cref="SerilogEnrichersMiddleware"/> to the application pipeline. The middleware pushes
    /// <c>UserId</c>, <c>AccountId</c>, and <c>CorrelationId</c> properties from the current
    /// <see cref="Headless.Abstractions.IRequestContext"/> into the Serilog log context for the duration
    /// of each request.
    /// </summary>
    /// <param name="builder">The application builder.</param>
    /// <returns><paramref name="builder"/> for chaining.</returns>
    /// <remarks>
    /// Call <see cref="AddSerilogEnrichers"/> first to register the middleware's dependencies.
    /// Place this call after authentication middleware so that <c>IRequestContext.User</c> is populated.
    /// </remarks>
    public static IApplicationBuilder UseSerilogEnrichers(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<SerilogEnrichersMiddleware>();
    }
}
