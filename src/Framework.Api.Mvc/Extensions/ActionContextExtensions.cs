// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace Framework.Api.Extensions;

[PublicAPI]
public static class ActionContextExtensions
{
    public static T GetRequiredService<T>(this FilterContext context)
        where T : class
    {
        return context.HttpContext.RequestServices.GetRequiredService<T>();
    }

    public static T? GetService<T>(this FilterContext context, T? defaultValue = null)
        where T : class
    {
        return context.HttpContext.RequestServices.GetService<T>() ?? defaultValue;
    }
}
