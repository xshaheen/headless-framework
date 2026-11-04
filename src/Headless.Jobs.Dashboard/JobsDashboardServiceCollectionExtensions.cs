// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using System.Text.Encodings.Web;
using Headless.Dashboard.Authentication;
using Headless.Jobs.Endpoints;
using Headless.Jobs.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;

namespace Headless.Jobs;

internal static class JobsDashboardServiceCollectionExtensions
{
    private const string _EmbeddedFileNamespace = "Headless.Jobs.wwwroot.dist";

    private static readonly JsonSerializerOptions _JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        // The config lands in an inline script and carries the request's path base, so escape markup characters.
        Encoder = JavaScriptEncoder.Default,
    };

    internal static void AddDashboardService<TTimeJob, TCronJob>(
        this IServiceCollection services,
        DashboardOptionsBuilder config,
        JobsRequestSerializationOptions requestSerializationOptions
    )
        where TTimeJob : TimeJobEntity<TTimeJob>, new()
        where TCronJob : CronJobEntity, new()
    {
        // Configure default Dashboard JSON options if not already configured
        if (config.DashboardJsonOptions == null)
        {
            config.DashboardJsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                Converters = { new StringToByteArrayConverter(requestSerializationOptions) },
            };
        }
        else
        {
            // Ensure StringToByteArrayConverter is always present
            if (!config.DashboardJsonOptions.Converters.Any(c => c is StringToByteArrayConverter))
            {
                config.DashboardJsonOptions.Converters.Add(new StringToByteArrayConverter(requestSerializationOptions));
            }
        }

        // Register the dashboard configuration for DI
        services.AddSingleton(config);

        services.AddRouting();
        services.AddSignalR();

        // The dashboard endpoints name this policy only when SetCorsOrigins or SetCorsPolicy configured one, because the
        // host's pipeline runs UseCors only if the host adds it.
        if (config.CorsPolicyBuilder is { } corsPolicyBuilder)
        {
            services.AddCors(options => options.AddPolicy(DashboardEndpoints.CorsPolicyName, corsPolicyBuilder));
        }
    }

    /// <summary>Maps the dashboard API, its SignalR hub, and the SPA under the host group at the base path.</summary>
    internal static void MapDashboard<TTimeJob, TCronJob>(
        this IEndpointRouteBuilder group,
        DashboardOptionsBuilder config
    )
        where TTimeJob : TimeJobEntity<TTimeJob>, new()
        where TCronJob : CronJobEntity, new()
    {
        var basePath = DashboardSpaHelper.NormalizeBasePath(config.BasePath);
        var embeddedFileProvider = new EmbeddedFileProvider(Assembly.GetExecutingAssembly(), _EmbeddedFileNamespace);

        group.MapDashboardEndpoints<TTimeJob, TCronJob>(config);
        group.MapDashboardSpa(
            "Jobs_Spa",
            embeddedFileProvider,
            (html, context) => ReplaceBasePath(html, context, basePath, config)
        );
    }

    internal static string ReplaceBasePath(
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

        var frontendBasePath = DashboardSpaHelper.CombinePathBase(pathBase, basePath);

        // Build the config object
        var authInfo = new
        {
            mode = config.Auth.Mode.ToString().ToLowerInvariant(),
            enabled = config.Auth.IsEnabled,
            // The server no longer expires browser sign-ins; 0 tells the SPA, which still reads this field, never to.
            sessionTimeout = 0,
        };

        var envConfig = new
        {
            basePath = frontendBasePath,
            backendDomain = config.BackendDomain,
            auth = authInfo,
        };

        var json = JsonSerializer.Serialize(envConfig, _JsonOptions);

        json = DashboardSpaHelper.SanitizeForInlineScript(json);

        // Add base tag for proper asset loading
        var baseTag = DashboardSpaHelper.BuildBaseTag(frontendBasePath);

        // Inline bootstrap: set JobsConfig and derive __dynamic_base__ (vite-plugin-dynamic-base)
        var script = $$"""
            <script>
            (function() {
            try {
                // Expose config
                window.JobsConfig = {{json}};

                // Derive dynamic base for vite-plugin-dynamic-base
                window.__dynamic_base__ = window.JobsConfig.basePath;
            } catch (e) { console.error('Runtime config injection failed:', e); }
            })();
            </script>
            """;

        var fullInjection = baseTag + script;
        return DashboardSpaHelper.InjectIntoHead(htmlContent, fullInjection);
    }
}
