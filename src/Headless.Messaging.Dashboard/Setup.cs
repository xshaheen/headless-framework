// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using Headless.Dashboard.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;

namespace Headless.Messaging.Dashboard;

public static class MessagingDashboardSetup
{
    private const string _EmbeddedFileNamespace = "Headless.Messaging.Dashboard.wwwroot.dist";

    private static readonly JsonSerializerOptions _JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.Default,
    };

    /// <summary>
    /// Configure the Messaging Dashboard middleware pipeline using app.Map branching.
    /// Matches the Jobs Dashboard middleware pipeline pattern.
    /// </summary>
    internal static void UseMessagingDashboard(this IApplicationBuilder app, MessagingDashboardOptionsBuilder config)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var embeddedFileProvider = new EmbeddedFileProvider(assembly, _EmbeddedFileNamespace);

        var basePath = DashboardSpaHelper.NormalizeBasePath(config.BasePath);

        app.Map(
            basePath,
            dashboardApp =>
            {
                // Execute pre-dashboard middleware
                config.PreDashboardMiddleware?.Invoke(dashboardApp);

                // CRITICAL: Serve static files FIRST, before any authentication
                dashboardApp.UseStaticFiles(
                    new StaticFileOptions
                    {
                        FileProvider = embeddedFileProvider,
                        OnPrepareResponse = ctx =>
                        {
                            if (
                                ctx.File.Name.EndsWith(".js", StringComparison.Ordinal)
                                || ctx.File.Name.EndsWith(".css", StringComparison.Ordinal)
                                || ctx.File.Name.EndsWith(".ico", StringComparison.Ordinal)
                                || ctx.File.Name.EndsWith(".png", StringComparison.Ordinal)
                            )
                            {
                                ctx.Context.Response.Headers.CacheControl = "public,max-age=3600";
                            }
                        },
                    }
                );

                // Set up routing and CORS
                dashboardApp.UseRouting();
                dashboardApp.UseCors("Messaging_Dashboard_CORS");

                // Add authentication + authorization middleware
                if (config.Auth.IsEnabled)
                {
                    dashboardApp.UseMiddleware<AuthMiddleware>();
                }

                dashboardApp.UseAuthorization();

                // Execute custom middleware if provided
                config.CustomMiddleware?.Invoke(dashboardApp);

                // Map Minimal API endpoints
                dashboardApp.UseEndpoints(endpoints =>
                {
                    endpoints.MapMessagingDashboardEndpoints(config);
                });

                // Execute post-dashboard middleware
                config.PostDashboardMiddleware?.Invoke(dashboardApp);

                // SPA fallback: serve modified index.html for unmatched routes
                dashboardApp.Use(
                    async (context, next) =>
                    {
                        await next();

                        if (context.Response.StatusCode == 404)
                        {
                            var file = embeddedFileProvider.GetFileInfo("index.html");
                            if (file.Exists)
                            {
                                await using var stream = file.CreateReadStream();
                                using var reader = new StreamReader(stream);
                                var htmlContent = await reader.ReadToEndAsync();

                                htmlContent = _ReplaceBasePath(htmlContent, context, basePath, config);

                                context.Response.ContentType = "text/html";
                                context.Response.StatusCode = 200;
                                await context.Response.WriteAsync(htmlContent);
                            }
                        }
                    }
                );
            }
        );
    }

    private static string _ReplaceBasePath(
        string htmlContent,
        HttpContext httpContext,
        string basePath,
        MessagingDashboardOptionsBuilder config
    )
    {
        if (string.IsNullOrEmpty(htmlContent))
        {
            return htmlContent ?? string.Empty;
        }

        var pathBase = httpContext.Request.PathBase.HasValue ? httpContext.Request.PathBase.Value : string.Empty;
        var frontendBasePath = DashboardSpaHelper.CombinePathBase(pathBase, basePath);

        var authInfo = new
        {
            mode = config.Auth.Mode.ToString().ToLowerInvariant(),
            enabled = config.Auth.IsEnabled,
            sessionTimeout = config.Auth.SessionTimeoutMinutes,
        };

        var envConfig = new
        {
            basePath = frontendBasePath,
            auth = authInfo,
            statsPollingInterval = config.StatsPollingInterval,
        };

        var json = JsonSerializer.Serialize(envConfig, _JsonOptions);
        json = DashboardSpaHelper.SanitizeForInlineScript(json);

        var baseTag = $"""<base href="{frontendBasePath}/" />""";

        var script = $$"""
            <script>
            (function() {
            try {
                window.MessagingConfig = {{json}};
                window.__dynamic_base__ = window.MessagingConfig.basePath;
            } catch (e) { console.error('Runtime config injection failed:', e); }
            })();
            </script>
            """;

        var fullInjection = baseTag + script;
        return DashboardSpaHelper.InjectIntoHead(htmlContent, fullInjection);
    }
}
