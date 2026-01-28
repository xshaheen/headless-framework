using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using Headless.Ticker.Authentication;
using Headless.Ticker.Endpoints;
using Headless.Ticker.Entities;
using Headless.Ticker.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;

namespace Headless.Ticker.DependencyInjection;

internal static partial class ServiceCollectionExtensions
{
    private static readonly JsonSerializerOptions _JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    [GeneratedRegex(@"(?is)<head\b[^>]*>", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex _HeadOpenRegex();

    internal static void AddDashboardService<TTimeTicker, TCronTicker>(
        this IServiceCollection services,
        DashboardOptionsBuilder config
    )
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        // Configure default Dashboard JSON options if not already configured
        if (config.DashboardJsonOptions == null)
        {
            config.DashboardJsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                Converters = { new StringToByteArrayConverter() },
            };
        }
        else
        {
            // Ensure StringToByteArrayConverter is always present
            if (!config.DashboardJsonOptions.Converters.Any(c => c is StringToByteArrayConverter))
            {
                config.DashboardJsonOptions.Converters.Add(new StringToByteArrayConverter());
            }
        }

        // Register the dashboard configuration for DI
        services.AddSingleton(config);

        services.AddRouting();
        services.AddSignalR();

        // The new authentication system is registered in ServiceExtensions.cs
        // This method is kept for backward compatibility with existing middleware pipeline

        services.AddAuthorization();

        services.AddCors(options =>
        {
            if (config.CorsPolicyBuilder is not null)
            {
                options.AddPolicy("TickerQ_Dashboard_CORS", config.CorsPolicyBuilder);
            }
        });
    }

    internal static void UseDashboardWithEndpoints<TTimeTicker, TCronTicker>(
        this IApplicationBuilder app,
        DashboardOptionsBuilder config
    )
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        // Get the assembly and set up the embedded file provider
        var assembly = Assembly.GetExecutingAssembly();
        var embeddedFileProvider = new EmbeddedFileProvider(assembly, "Headless.Ticker.Dashboard.wwwroot.dist");

        // Validate and normalize base path
        var basePath = _NormalizeBasePath(config.BasePath);

        // Map a branch for the basePath to properly isolate dashboard
        app.Map(
            basePath,
            dashboardApp =>
            {
                // Execute pre-dashboard middleware
                config.PreDashboardMiddleware?.Invoke(dashboardApp);

                // CRITICAL: Serve static files FIRST, before any authentication
                // This ensures static assets (JS, CSS, images) are served without auth challenges
                dashboardApp.UseStaticFiles(
                    new StaticFileOptions
                    {
                        FileProvider = embeddedFileProvider,
                        OnPrepareResponse = ctx =>
                        {
                            // Cache static assets for 1 hour
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
                dashboardApp.UseCors("TickerQ_Dashboard_CORS");

                // Add authentication middleware (only protects API endpoints)
                if (config.Auth.IsEnabled)
                {
                    dashboardApp.UseMiddleware<AuthMiddleware>();
                }

                // Execute custom middleware if provided
                config.CustomMiddleware?.Invoke(dashboardApp);

                // Map Minimal API endpoints and SignalR hub
                dashboardApp.UseEndpoints(endpoints =>
                {
                    endpoints.MapDashboardEndpoints<TTimeTicker, TCronTicker>(config);
                });

                // Execute post-dashboard middleware
                config.PostDashboardMiddleware?.Invoke(dashboardApp);

                // SPA fallback middleware: if no route is matched, serve the modified index.html
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

                                // Inject the base tag and other replacements into the HTML
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

    private static string _NormalizeBasePath(string basePath)
    {
        if (string.IsNullOrEmpty(basePath))
        {
            return "/";
        }

        if (!basePath.StartsWith('/'))
        {
            basePath = "/" + basePath;
        }

        return basePath.TrimEnd('/');
    }

    private static string _ReplaceBasePath(
        string htmlContent,
        HttpContext httpContext,
        string basePath,
        DashboardOptionsBuilder config
    )
    {
        if (string.IsNullOrEmpty(htmlContent))
        {
            return htmlContent ?? string.Empty;
        }

        // Compute the frontend base path as PathBase + backend basePath.
        // This ensures correct URLs when the host app uses UsePathBase.
        var pathBase = httpContext.Request.PathBase.HasValue ? httpContext.Request.PathBase.Value : string.Empty;

        var frontendBasePath = _CombinePathBase(pathBase, basePath);

        // Build the config object
        var authInfo = new
        {
            mode = config.Auth.Mode.ToString().ToLowerInvariant(),
            enabled = config.Auth.IsEnabled,
            sessionTimeout = config.Auth.SessionTimeoutMinutes,
        };

        var envConfig = new
        {
            basePath = frontendBasePath,
            backendDomain = config.BackendDomain,
            auth = authInfo,
        };

        // Serialize without over-escaping, but make sure it won't break </script>
        var json = JsonSerializer.Serialize(envConfig, _JsonOptions);

        json = _SanitizeForInlineScript(json);

        // Add base tag for proper asset loading
        var baseTag = $"""<base href="{frontendBasePath}/" />""";

        // Inline bootstrap: set TickerQConfig and derive __dynamic_base__ (vite-plugin-dynamic-base)
        var script = $$"""
            <script>
            (function() {
            try {
                // Expose config
                window.TickerQConfig = {{json}};

                // Derive dynamic base for vite-plugin-dynamic-base
                window.__dynamic_base__ = window.TickerQConfig.basePath;
            } catch (e) { console.error('Runtime config injection failed:', e); }
            })();
            </script>
            """;

        var fullInjection = baseTag + script;
        // Prefer inject immediately after opening <head ...>
        var headOpen = _HeadOpenRegex().Match(htmlContent);
        if (headOpen.Success)
        {
            return htmlContent.Insert(headOpen.Index + headOpen.Length, fullInjection);
        }

        // Fallback: just before </head>
        var closeIdx = htmlContent.IndexOf("</head>", StringComparison.OrdinalIgnoreCase);
        if (closeIdx >= 0)
        {
            return htmlContent.Insert(closeIdx, fullInjection);
        }

        // Last resort: prepend (ensures script runs early)
        return fullInjection + htmlContent;
    }

    private static string _CombinePathBase(string? pathBase, string? basePath)
    {
        pathBase ??= string.Empty;
        basePath ??= "/";

        if (string.IsNullOrEmpty(basePath) || string.Equals(basePath, "/", StringComparison.Ordinal))
        {
            return string.IsNullOrEmpty(pathBase) ? "/" : pathBase;
        }

        if (string.IsNullOrEmpty(pathBase))
        {
            return basePath;
        }

        // If basePath already includes the pathBase prefix, treat it as the full frontend path
        // This prevents /cool-app/cool-app/... and similar double-prefix issues when users
        // configure BasePath with the full URL segment.
        if (basePath.StartsWith(pathBase, StringComparison.OrdinalIgnoreCase))
        {
            return basePath;
        }

        // Normalize to avoid double slashes
        if (pathBase.EndsWith('/'))
        {
            pathBase = pathBase.TrimEnd('/');
        }

        // basePath is already normalized to start with '/'
        return pathBase + basePath;
    }

    /// <summary>
    /// Prevents &lt;/script&gt; in JSON strings from prematurely closing the inline script.
    /// </summary>
    private static string _SanitizeForInlineScript(string json) =>
        json.Replace("</script", "<\\/script", StringComparison.OrdinalIgnoreCase);
}
