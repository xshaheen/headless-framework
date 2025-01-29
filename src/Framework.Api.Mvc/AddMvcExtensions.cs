// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Api.Mvc.Controllers;
using Framework.Api.Mvc.Options;
using Microsoft.Extensions.DependencyInjection;

namespace Framework.Api.Mvc;

public static class AddMvcExtensions
{
    public static void AddFrameworkMvcOptions(this IServiceCollection services)
    {
        services.ConfigureOptions<ConfigureMvcJsonOptions>();
        services.ConfigureOptions<ConfigureMvcApiOptions>();
        services.AddSingleton<MvcProblemDetailsNormalizer>();
    }
}
