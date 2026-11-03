// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using System.Text.Encodings.Web;
using Headless.Checks;
using Headless.Dashboard.Authentication;
using Headless.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;

namespace Headless.Messaging.Dashboard;

public static class SetupMessagingDashboard
{
    private const string _EmbeddedFileNamespace = "Headless.Messaging.Dashboard.wwwroot.dist";

    private static readonly FileExtensionContentTypeProvider _ContentTypeProvider = new();

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
                        OnPrepareResponse = ctx => _ApplyAssetCacheHeaders(ctx.Context, ctx.File.Name),
                    }
                );

                // Endpoints carry RequireCors metadata, so the CORS middleware must always run (an endpoint
                // with CORS metadata and no middleware throws at request time). The registered policy is empty
                // by default (same-origin only) and reflects the configured origins when
                // SetCorsOrigins / SetCorsPolicy was used.
                dashboardApp.UseRouting();
                dashboardApp.UseCors(MessagingDashboardEndpoints.CorsPolicyName);

                // Add authentication + authorization middleware
                if (config.Auth.IsEnabled)
                {
                    dashboardApp.UseMiddleware<AuthMiddleware>(MessagingDashboardOptionsBuilder.AuthenticationName);
                }

                dashboardApp.UseAuthorization();

                // Execute custom middleware if provided
                config.CustomMiddleware?.Invoke(dashboardApp);

                // Map Minimal API endpoints
                dashboardApp.UseEndpoints(endpoints => endpoints.MapMessagingDashboardEndpoints(config));

                // Execute post-dashboard middleware
                config.PostDashboardMiddleware?.Invoke(dashboardApp);

                // SPA fallback: serve modified index.html for unmatched routes
                dashboardApp.Use(
                    async (context, next) =>
                    {
                        await next().ConfigureAwait(false);

                        if (context.Response.StatusCode == 404)
                        {
                            var htmlContent = await _RenderShellAsync(embeddedFileProvider, context, basePath, config)
                                .ConfigureAwait(false);

                            if (htmlContent is not null)
                            {
                                context.Response.ContentType = ContentTypes.Texts.Html;
                                context.Response.StatusCode = 200;
                                await context.Response.WriteAsync(htmlContent).ConfigureAwait(false);
                            }
                        }
                    }
                );
            }
        );
    }

    /// <summary>
    /// Mounts the Messaging Dashboard on the host's endpoint routing instead of the <c>app.Map(basePath)</c> branch that
    /// <c>UseDashboard(...)</c> appends after the host pipeline, so the host's <c>UseAuthorization</c> evaluates the
    /// dashboard's own policy rather than the host's fallback policy.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Calling this method is the opt-in: once it has run, the dashboard's startup filter no longer adds the branch.
    /// The SPA shell, its static assets, and the <c>/api/auth/info</c>, <c>/api/auth/validate</c>, and
    /// <c>/api/health</c> endpoints are mapped with <c>AllowAnonymous</c>, so a browser loads the shell without a
    /// credential; the shell carries only the base path, the auth mode, and the polling interval. Under
    /// <c>WithHostAuthentication(policy)</c> every other <c>{basePath}/api</c> endpoint requires that policy, or the
    /// default policy when none is named. Under Basic, API key, or custom authentication the API is anonymous to the
    /// host and the dashboard's own credential is checked by an endpoint filter, as the branch mount checks it;
    /// <c>WithNoAuth()</c> leaves it open.
    /// </para>
    /// <para>
    /// The host's pipeline must run <c>UseAuthentication</c> and <c>UseAuthorization</c> (for host authentication) and
    /// <c>UseCors</c> (only when <c>SetCorsOrigins</c> or <c>SetCorsPolicy</c> was configured) between routing and
    /// endpoints. The branch-only hooks <c>PreDashboardMiddleware</c>, <c>CustomMiddleware</c>, and
    /// <c>PostDashboardMiddleware</c> have no place in the host pipeline and are rejected.
    /// </para>
    /// <para>
    /// The <c>Headless</c> prefix marks the Headless-owned dashboard, as <c>AddHeadlessMessaging</c> does.
    /// </para>
    /// </remarks>
    /// <param name="endpoints">The host's endpoint route builder, such as the <c>WebApplication</c>.</param>
    /// <returns>A builder for conventions that apply to every dashboard endpoint.</returns>
    /// <exception cref="InvalidOperationException">
    /// The dashboard is not registered through <c>UseDashboard(...)</c>, it is already mapped, or a branch-only
    /// middleware hook is configured.
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

        if (
            config.PreDashboardMiddleware is not null
            || config.CustomMiddleware is not null
            || config.PostDashboardMiddleware is not null
        )
        {
            throw new InvalidOperationException(
                "PreDashboardMiddleware, CustomMiddleware, and PostDashboardMiddleware configure the app.Map branch "
                    + "mount and cannot run under MapHeadlessMessagingDashboard(). Put that logic in the host pipeline "
                    + "or in endpoint conventions on the returned builder."
            );
        }

        if (config.EndpointMounted)
        {
            throw new InvalidOperationException("MapHeadlessMessagingDashboard() was already called for this host.");
        }

        config.EndpointMounted = true;

        var embeddedFileProvider = new EmbeddedFileProvider(Assembly.GetExecutingAssembly(), _EmbeddedFileNamespace);
        var basePath = DashboardSpaHelper.NormalizeBasePath(config.BasePath);
        var group = endpoints.MapGroup(basePath);

        group.MapMessagingDashboardEndpoints(config, hostEndpointMount: true);

        group
            .MapMethods(
                "/{**path}",
                [HttpMethods.Get, HttpMethods.Head],
                (HttpContext context, string? path) =>
                    _ServeSpaAsync(context, path, embeddedFileProvider, basePath, config)
            )
            .WithName("Messaging_Spa")
            .AllowAnonymous()
            .ExcludeFromDescription();

        return group;
    }

    /// <summary>
    /// Serves an embedded asset, or the SPA shell for any other path so client-side routes deep-link. Paths under
    /// <c>api</c> never get the shell: an unmatched API route is a 404, not an anonymous HTML page.
    /// </summary>
    private static async Task<IResult> _ServeSpaAsync(
        HttpContext context,
        string? path,
        EmbeddedFileProvider fileProvider,
        string basePath,
        MessagingDashboardOptionsBuilder config
    )
    {
        if (
            path is not null
            && (
                string.Equals(path, "api", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("api/", StringComparison.OrdinalIgnoreCase)
            )
        )
        {
            return Results.NotFound();
        }

        if (!string.IsNullOrEmpty(path) && !string.Equals(path, "index.html", StringComparison.OrdinalIgnoreCase))
        {
            var file = fileProvider.GetFileInfo(path);
            if (file is { Exists: true, IsDirectory: false })
            {
                if (!_ContentTypeProvider.TryGetContentType(file.Name, out var contentType))
                {
                    contentType = "application/octet-stream";
                }

                _ApplyAssetCacheHeaders(context, file.Name);
                return Results.Stream(file.CreateReadStream(), contentType, lastModified: file.LastModified);
            }
        }

        var html = await _RenderShellAsync(fileProvider, context, basePath, config).ConfigureAwait(false);

        return html is null ? Results.NotFound() : Results.Content(html, ContentTypes.Texts.Html);
    }

    private static async Task<string?> _RenderShellAsync(
        EmbeddedFileProvider fileProvider,
        HttpContext context,
        string basePath,
        MessagingDashboardOptionsBuilder config
    )
    {
        var file = fileProvider.GetFileInfo("index.html");
        if (!file.Exists)
        {
            return null;
        }

        await using var stream = file.CreateReadStream();
        var htmlContent = await stream.GetAllTextAsync().ConfigureAwait(false);

        return _ReplaceBasePath(htmlContent, context, basePath, config);
    }

    private static void _ApplyAssetCacheHeaders(HttpContext context, string fileName)
    {
        if (
            fileName.EndsWith(".js", StringComparison.Ordinal)
            || fileName.EndsWith(".css", StringComparison.Ordinal)
            || fileName.EndsWith(".ico", StringComparison.Ordinal)
            || fileName.EndsWith(".png", StringComparison.Ordinal)
        )
        {
            context.Response.Headers.CacheControl = "public,max-age=3600";
        }
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
