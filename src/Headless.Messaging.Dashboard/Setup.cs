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
    /// SPA shell, its static assets, the sign-in endpoints (<c>/auth/sign-in</c>, <c>/api/auth/session</c>,
    /// <c>/api/auth/sign-out</c>), and the <c>/api/auth/info</c>, <c>/api/auth/validate</c>, and <c>/api/health</c>
    /// endpoints are anonymous, so a browser loads the shell without a credential; the shell carries only the base
    /// path, the auth mode, whether sign-in is available, and the polling interval.
    /// </para>
    /// <para>
    /// Under host authentication every other <c>{basePath}/api</c> endpoint requires the dashboard's session cookie,
    /// issued from the host's own sign-in, for a user who passes the view policy, or the host's default policy when
    /// none is named; the operator actions (requeue, delete, re-execute, hold, release, revoke, dispatch now, and
    /// ping) also require the operate policy. Under Basic or API key authentication the API requires the dashboard's
    /// own credential, checked by the dashboard's authentication scheme; <c>WithNoAuth()</c> leaves it open. In every
    /// mode a state-changing request must carry <c>X-Requested-With</c>, or it is answered with <c>400</c>.
    /// </para>
    /// <para>
    /// The host's pipeline must run <c>UseAuthorization</c> between routing and endpoints (a <c>WebApplication</c>
    /// adds it when the host does not), <c>UseAuthentication</c> before it for host authentication, and
    /// <c>UseCors</c> only when <c>SetCorsOrigins</c> or <c>SetCorsPolicy</c> was configured. Host-specific behavior
    /// for the dashboard, such as rate limiting or an extra policy, goes on the returned builder.
    /// </para>
    /// <para>
    /// A host that calls <c>UseAuthentication</c> must also call <c>UseAuthorization</c> after it: the
    /// <c>UseAuthorization</c> a <c>WebApplication</c> adds on its own runs ahead of the host's
    /// <c>UseAuthentication</c>, before host authentication has signed the user in.
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

        // Session state (signed in, operator name, may operate) is not here: the shell is anonymous and may load without
        // the session cookie, so the SPA reads it from api/auth/info instead.
        var authInfo = new
        {
            mode = config.Auth.Mode.ToString().ToLowerInvariant(),
            enabled = config.Auth.IsEnabled,
            signInAvailable = config.Auth.Mode == AuthMode.Host
                && !string.IsNullOrEmpty(config.Auth.Host.ChallengeScheme),
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
