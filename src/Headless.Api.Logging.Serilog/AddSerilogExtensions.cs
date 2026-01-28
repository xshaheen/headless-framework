// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Logging;

[PublicAPI]
public static class AddSerilogExtensions
{
    /// <summary>Adds the serilog enrichers middleware.</summary>
    public static IServiceCollection AddHeadlessSerilogEnrichers(this IServiceCollection services)
    {
        return services.AddScoped<SerilogEnrichersMiddleware>();
    }

    public static IApplicationBuilder UseHeadlessSerilogEnrichers(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<SerilogEnrichersMiddleware>();
    }
}
