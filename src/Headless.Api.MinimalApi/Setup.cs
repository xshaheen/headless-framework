// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api.Options;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Api;

[PublicAPI]
public static class SetupMinimalApi
{
    public static IServiceCollection ConfigureMinimalApi(this IServiceCollection services)
    {
        services.ConfigureOptions<ConfigureMinimalApiJsonOptions>();

        return services;
    }

    public static void ConfigureMinimalApi(this WebApplicationBuilder builder)
    {
        builder.Services.ConfigureOptions<ConfigureMinimalApiJsonOptions>();
    }
}
