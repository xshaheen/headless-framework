// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Framework.Api.Logging.Serilog;

public static class AddSerilogExtensions
{
    /// <summary>Adds the serilog enrichers middleware.</summary>
    public static IServiceCollection AddSerilogEnrichers(this IServiceCollection services)
    {
        return services.AddScoped<SerilogEnrichersMiddleware>();
    }

    public static IApplicationBuilder UseCustomSerilogEnrichers(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<SerilogEnrichersMiddleware>();
    }
}
