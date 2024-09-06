using Framework.Kernel.BuildingBlocks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Framework.Api.Core.Extensions.Cookies;

public static class ConfigureApplicationCookiesExtensions
{
    public static void ConfigureApiApplicationCookie(this IServiceCollection services)
    {
        services.ConfigureApplicationCookie(options =>
        {
            options.Events.OnRedirectToLogin = context =>
            {
                context.Response.Headers[HttpHeaderNames.Locale] = context.RedirectUri;
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;

                return Task.CompletedTask;
            };

            options.Events.OnRedirectToAccessDenied = context =>
            {
                context.Response.Headers[HttpHeaderNames.Locale] = context.RedirectUri;
                context.Response.StatusCode = StatusCodes.Status403Forbidden;

                return Task.CompletedTask;
            };
        });
    }
}
