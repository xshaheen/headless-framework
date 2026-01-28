// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Constants;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Api.Extensions.Cookies;

public static class ConfigureApplicationCookiesExtensions
{
    public static void ConfigureApiApplicationCookie(this IServiceCollection services)
    {
        services.ConfigureApplicationCookie(options =>
        {
            options.Events.OnRedirectToLogin = context =>
            {
                context.Response.Headers[HttpHeaderNames.Location] = context.RedirectUri;
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;

                return Task.CompletedTask;
            };

            options.Events.OnRedirectToAccessDenied = context =>
            {
                context.Response.Headers[HttpHeaderNames.Location] = context.RedirectUri;
                context.Response.StatusCode = StatusCodes.Status403Forbidden;

                return Task.CompletedTask;
            };
        });
    }
}
