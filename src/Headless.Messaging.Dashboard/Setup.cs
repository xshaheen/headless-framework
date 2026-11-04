// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using System.Text.Encodings.Web;
using Headless.Checks;
using Headless.Dashboard.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;

namespace Headless.Messaging.Dashboard;

public static class SetupMessagingDashboard
{
    private const string _EmbeddedFileNamespace = "Headless.Messaging.Dashboard.wwwroot.dist";

    private static readonly JsonSerializerOptions _JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.Default,
    };

    /// <summary>
    /// Mounts the Messaging Dashboard registered by <c>UseDashboard(...)</c> on the host's endpoint routing, under its
    /// base path. This is the only way the dashboard is served: a host that registers it and never calls this method
    /// fails to start.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The host's <c>UseAuthorization</c> evaluates the dashboard's access rule, never the host's fallback policy. The
    /// SPA shell, its static assets, and the <c>/api/auth/info</c>, <c>/api/auth/validate</c>, and
    /// <c>/api/health</c> endpoints are anonymous, so a browser loads the shell without a credential; the shell carries
    /// only the base path, the auth mode, and the polling interval. Every other <c>{basePath}/api</c> endpoint requires
    /// the policy <c>WithHostAuthentication(policy)</c> names, or the host's default policy when none is named. Under
    /// Basic, API key, or custom authentication it requires the dashboard's own credential, checked by the dashboard's
    /// authentication scheme; <c>WithNoAuth()</c> leaves it open.
    /// </para>
    /// <para>
    /// The host's pipeline must run <c>UseAuthorization</c> between routing and endpoints (a <c>WebApplication</c>
    /// adds it when the host does not), <c>UseAuthentication</c> before it for host authentication, and
    /// <c>UseCors</c> only when <c>SetCorsOrigins</c> or <c>SetCorsPolicy</c> was configured. Host-specific behavior
    /// for the dashboard, such as rate limiting or an extra policy, goes on the returned builder.
    /// </para>
    /// <para>
    /// The <c>Headless</c> prefix marks the Headless-owned dashboard, as <c>AddHeadlessMessaging</c> does.
    /// </para>
    /// </remarks>
    /// <param name="endpoints">The host's endpoint route builder, such as the <c>WebApplication</c>.</param>
    /// <returns>A builder for conventions that apply to every dashboard endpoint.</returns>
    /// <exception cref="InvalidOperationException">
    /// The dashboard is not registered through <c>UseDashboard(...)</c>, it is already mapped, or its base path is the
    /// root.
    /// </exception>
    public static IEndpointConventionBuilder MapHeadlessMessagingDashboard(this IEndpointRouteBuilder endpoints)
    {
        Argument.IsNotNull(endpoints);

        var config =
            endpoints.ServiceProvider.GetService<MessagingDashboardOptionsBuilder>()
            ?? throw new InvalidOperationException(
                "The Messaging dashboard is not registered. Call UseDashboard(...) inside AddHeadlessMessaging(...) "
                    + "before MapHeadlessMessagingDashboard()."
            );

        var basePath = DashboardSpaHelper.NormalizeMountBasePath(
            config.BasePath,
            nameof(MapHeadlessMessagingDashboard)
        );

        if (config.EndpointMounted)
        {
            throw new InvalidOperationException("MapHeadlessMessagingDashboard() was already called for this host.");
        }

        config.EndpointMounted = true;

        var embeddedFileProvider = new EmbeddedFileProvider(Assembly.GetExecutingAssembly(), _EmbeddedFileNamespace);
        var group = endpoints.MapGroup(basePath);

        group.MapMessagingDashboardEndpoints(config);
        group.MapDashboardSpa(
            "Messaging_Spa",
            embeddedFileProvider,
            (html, context) => _ReplaceBasePath(html, context, basePath, config)
        );

        return group;
    }

    private static string _ReplaceBasePath(
        string? htmlContent,
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

        var baseTag = DashboardSpaHelper.BuildBaseTag(frontendBasePath);

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
