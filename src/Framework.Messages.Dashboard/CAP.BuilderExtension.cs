// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.CompilerServices;
using DotNetCore.CAP.Dashboard;
using Framework.Checks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;

namespace Framework.Messages;

public static class CapBuilderExtension
{
    private const string _EmbeddedFileNamespace = "Framework.Messages.Dashboard.wwwroot.dist";

    internal static IApplicationBuilder UseCapDashboard(this IApplicationBuilder app)
    {
        Argument.IsNotNull(app);

        var provider = app.ApplicationServices;

        var options = provider.GetService<DashboardOptions>();

        if (options != null)
        {
            app.UseStaticFiles(
                new StaticFileOptions
                {
                    RequestPath = options.PathMatch,
                    FileProvider = new EmbeddedFileProvider(options.GetType().Assembly, _EmbeddedFileNamespace),
                }
            );

            var endpointRouteBuilder = (IEndpointRouteBuilder)app.Properties["__EndpointRouteBuilder"]!;

            endpointRouteBuilder
                .MapGet(
                    pattern: options.PathMatch,
                    requestDelegate: httpContext =>
                    {
                        var path = httpContext.Request.Path.Value;

                        var redirectUrl =
                            string.IsNullOrEmpty(path) || path.EndsWith('/')
                                ? "index.html"
                                : $"{path.Split('/')[^1]}/index.html";

                        httpContext.Response.StatusCode = 301;
                        httpContext.Response.Headers.Location = redirectUrl;
                        return Task.CompletedTask;
                    }
                )
                .AllowAnonymousIf(options.AllowAnonymousExplicit, options.AuthorizationPolicy);

            endpointRouteBuilder
                .MapGet(
                    pattern: options.PathMatch + "/index.html",
                    requestDelegate: async httpContext =>
                    {
                        httpContext.Response.StatusCode = 200;
                        httpContext.Response.ContentType = "text/html;charset=utf-8";

                        await using var stream = options
                            .GetType()
                            .Assembly.GetManifestResourceStream(_EmbeddedFileNamespace + ".index.html");

                        if (stream == null)
                            throw new InvalidOperationException();

                        using var sr = new StreamReader(stream);
                        var htmlBuilder = new StringBuilder(await sr.ReadToEndAsync());
                        htmlBuilder.Replace("%(servicePrefix)", options.PathBase + options.PathMatch + "/api");
                        htmlBuilder.Replace(
                            "%(pollingInterval)",
                            options.StatsPollingInterval.ToString(CultureInfo.InvariantCulture)
                        );
                        await httpContext.Response.WriteAsync(htmlBuilder.ToString(), Encoding.UTF8);
                    }
                )
                .AllowAnonymousIf(options.AllowAnonymousExplicit, options.AuthorizationPolicy);

            new RouteActionProvider(endpointRouteBuilder, options).MapDashboardRoutes();
        }

        return app;
    }

    internal static IEndpointConventionBuilder AllowAnonymousIf(
        this IEndpointConventionBuilder builder,
        bool allowAnonymous,
        params string?[] authorizationPolicies
    )
    {
        if (allowAnonymous)
        {
            return builder.AllowAnonymous();
        }

        var validAuthorizationPolicies = authorizationPolicies
            .Where(policy => !string.IsNullOrEmpty(policy))!
            .ToArray<string>();

        if (validAuthorizationPolicies.Length == 0)
        {
            throw new InvalidOperationException(
                "If Dashboard Options does not explicitly allow anonymous requests, the Authorization Policy must be configured."
            );
        }

        return builder.RequireAuthorization(validAuthorizationPolicies);
    }
}
