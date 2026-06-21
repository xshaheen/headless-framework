// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Constants;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Api.Extensions.Cookies;

/// <summary>
/// Extension methods for configuring ASP.NET Core Identity's application cookie for API usage.
/// </summary>
public static class ConfigureApplicationCookiesExtensions
{
    /// <summary>
    /// Overrides the default ASP.NET Core Identity cookie redirect behavior so that
    /// unauthenticated and access-denied redirects return HTTP status codes instead of HTML
    /// redirect responses:
    /// <list type="bullet">
    /// <item><description>Login redirect → 401 with the <c>Location</c> header preserved.</description></item>
    /// <item><description>Access-denied redirect → 403 with the <c>Location</c> header preserved.</description></item>
    /// </list>
    /// This makes cookie-authenticated APIs behave correctly for non-browser clients.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <remarks>
    /// Requires <c>AddIdentity</c> or <c>AddAuthentication</c> to have been called first so
    /// the application cookie options are already registered.
    /// </remarks>
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
