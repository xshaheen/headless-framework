// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Api.Options;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Framework.Api;

public static class AddMinimalApiExtensions
{
    public static void ConfigureHeadlessMinimalApi(this IServiceCollection services)
    {
        services.ConfigureOptions<ConfigureMinimalApiJsonOptions>();
    }

    public static void ConfigureHeadlessMinimalApi(this WebApplicationBuilder builder)
    {
        builder.Services.ConfigureOptions<ConfigureMinimalApiJsonOptions>();
    }
}
