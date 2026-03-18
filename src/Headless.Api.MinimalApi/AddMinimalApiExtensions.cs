// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api.Options;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Api;

public static class AddMinimalApiExtensions
{
    public static void ConfigureMinimalApi(this IServiceCollection services)
    {
        services.ConfigureOptions<ConfigureMinimalApiJsonOptions>();
    }

    public static void ConfigureMinimalApi(this WebApplicationBuilder builder)
    {
        builder.Services.ConfigureOptions<ConfigureMinimalApiJsonOptions>();
    }
}
