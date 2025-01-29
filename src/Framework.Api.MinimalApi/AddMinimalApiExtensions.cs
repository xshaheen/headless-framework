// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Api.MinimalApi.Options;
using Microsoft.Extensions.DependencyInjection;

namespace Framework.Api.MinimalApi;

public static class AddMinimalApiExtensions
{
    public static void AddFrameworkMinimalApiOptions(this IServiceCollection services)
    {
        services.ConfigureOptions<ConfigureMinimalApiJsonOptions>();
    }
}
