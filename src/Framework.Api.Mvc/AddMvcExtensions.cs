// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Api.Options;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Framework.Api;

public static class AddMvcExtensions
{
    public static IServiceCollection ConfigureHeadlessMvc(this IServiceCollection services)
    {
        services.ConfigureOptions<ConfigureMvcJsonOptions>();
        services.ConfigureOptions<ConfigureMvcApiOptions>();

        return services;
    }

    public static WebApplicationBuilder ConfigureHeadlessMvc(this WebApplicationBuilder builder)
    {
        builder.Services.ConfigureOptions<ConfigureMvcJsonOptions>();
        builder.Services.ConfigureOptions<ConfigureMvcApiOptions>();

        return builder;
    }
}
