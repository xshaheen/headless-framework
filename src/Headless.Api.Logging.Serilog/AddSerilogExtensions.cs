// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Builder;

namespace Headless.Logging;

[PublicAPI]
public static class AddSerilogExtensions
{
    /// <summary>
    /// Adds <see cref="SerilogEnrichersMiddleware"/> to the application pipeline. The middleware pushes
    /// <c>UserId</c>, <c>AccountId</c>, and <c>CorrelationId</c> properties from the current
    /// <see cref="Headless.Api.IRequestContext"/> into the Serilog log context for the duration
    /// of each request.
    /// </summary>
    /// <param name="builder">The application builder.</param>
    /// <returns><paramref name="builder"/> for chaining.</returns>
    /// <remarks>
    /// Needs no service registration of its own: <see cref="Headless.Api.IRequestContext"/> comes from
    /// <c>AddHeadless()</c>. Place this call after authentication middleware so that <c>IRequestContext.User</c>
    /// is populated.
    /// </remarks>
    public static IApplicationBuilder UseHeadlessSerilogEnrichers(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<SerilogEnrichersMiddleware>();
    }
}
