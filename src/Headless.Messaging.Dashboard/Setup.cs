// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Headless.Checks;
using Headless.Dashboard;
using Headless.Hosting;
using Headless.Messaging.Internal;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.Dashboard;

public static class SetupMessagingDashboard
{
    private const string _EmbeddedFileNamespace = "Headless.Messaging.Dashboard.wwwroot.dist";

    private const string _MissingMessaging =
        "The Messaging dashboard module (AddMessaging) needs Headless Messaging. Call "
        + "services.AddHeadlessMessaging(...) to register it.";

    private static readonly JsonSerializerOptions _JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.Default,
    };

    extension(HeadlessDashboardSetupBuilder dashboard)
    {
        /// <summary>
        /// Adds the Messaging module to the unified dashboard under <see cref="MessagingDashboardOptions.ModuleId"/>:
        /// published and received messages of the main store and every additional outbox <c>AddHeadlessMessaging</c>
        /// configured, with Needs you, Overview figures, sidebar counts, id lookup, payloads, and live changes from
        /// every node.
        /// </summary>
        /// <remarks>
        /// The module reads the stores <c>AddHeadlessMessaging</c> registered, so it takes none. A host that adds it
        /// without calling <c>AddHeadlessMessaging</c> fails to start with a message naming that call. It serves beside
        /// the standalone dashboard <c>UseDashboard(...)</c> registers; the two share nothing but the stores.
        /// </remarks>
        /// <returns>The same builder for chaining.</returns>
        /// <exception cref="InvalidOperationException">A module with the id <c>messaging</c> is already added.</exception>
        public HeadlessDashboardSetupBuilder AddMessaging()
        {
            return dashboard.AddMessaging(static _ => { });
        }

        /// <summary>Adds the Messaging module configured by <paramref name="setupAction"/>.</summary>
        /// <param name="setupAction">Configures the module's policies, node name, and store reads.</param>
        /// <returns>The same builder for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="setupAction"/> is <see langword="null"/>.</exception>
        /// <exception cref="InvalidOperationException">A module with the id <c>messaging</c> is already added.</exception>
        public HeadlessDashboardSetupBuilder AddMessaging(Action<MessagingDashboardOptions> setupAction)
        {
            Argument.IsNotNull(setupAction);

            dashboard.Services.Configure<MessagingDashboardOptions, MessagingDashboardOptionsValidator>(setupAction);

            return _AddMessagingModule(dashboard);
        }

        /// <summary>Adds the Messaging module configured by <paramref name="setupAction"/>, with the service provider.</summary>
        /// <param name="setupAction">Configures the module's policies, node name, and store reads.</param>
        /// <returns>The same builder for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="setupAction"/> is <see langword="null"/>.</exception>
        /// <exception cref="InvalidOperationException">A module with the id <c>messaging</c> is already added.</exception>
        public HeadlessDashboardSetupBuilder AddMessaging(
            Action<MessagingDashboardOptions, IServiceProvider> setupAction
        )
        {
            Argument.IsNotNull(setupAction);

            dashboard.Services.Configure<MessagingDashboardOptions, MessagingDashboardOptionsValidator>(setupAction);

            return _AddMessagingModule(dashboard);
        }

        /// <summary>
        /// Adds the Messaging module bound from <paramref name="configuration"/>, with the keys <c>ViewPolicy</c>,
        /// <c>OperatePolicy</c>, <c>NodeName</c>, <c>ChangeOverlapWindow</c>, <c>ChangePageSize</c>,
        /// <c>StoreTimeout</c>, <c>ForwardedAccess</c>, and <c>Forwarding</c> (<c>Key</c>, <c>Policy</c>,
        /// <c>AllowInsecurePeers</c>).
        /// </summary>
        /// <param name="configuration">The configuration section to bind.</param>
        /// <returns>The same builder for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configuration"/> is <see langword="null"/>.</exception>
        /// <exception cref="InvalidOperationException">A module with the id <c>messaging</c> is already added.</exception>
        public HeadlessDashboardSetupBuilder AddMessaging(IConfiguration configuration)
        {
            Argument.IsNotNull(configuration);

            dashboard.Services.Configure<MessagingDashboardOptions, MessagingDashboardOptionsValidator>(configuration);

            return _AddMessagingModule(dashboard);
        }

        /// <summary>
        /// Adds the Messaging module to a dashboard-only host: one that runs no Messaging of its own and shows the
        /// Messaging nodes service discovery lists, each through forwarding.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The host needs a discovery provider and forwarding (<see cref="MessagingForwardingOptions.Key"/> or
        /// <see cref="MessagingForwardingOptions.Policy"/>) with the same settings as the nodes, and fails to start
        /// without either. It opens on the first node discovery lists; with none reachable, its Overview names
        /// "No Messaging node reachable" with what discovery found.
        /// </para>
        /// <para>
        /// Set <see cref="MessagingDashboardOptions.NodeName"/> to a name no node uses: a node named like this host
        /// is never called.
        /// </para>
        /// </remarks>
        /// <param name="setupAction">Configures the module's policies, node name, and forwarding.</param>
        /// <returns>The same builder for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="setupAction"/> is <see langword="null"/>.</exception>
        /// <exception cref="InvalidOperationException">A module with the id <c>messaging</c> is already added.</exception>
        public HeadlessDashboardSetupBuilder AddMessagingStandalone(Action<MessagingDashboardOptions> setupAction)
        {
            Argument.IsNotNull(setupAction);

            dashboard.Services.Configure<MessagingDashboardOptions, MessagingDashboardOptionsValidator>(setupAction);

            return _AddMessagingModule(dashboard, standalone: true);
        }

        /// <summary>Adds the Messaging module to a dashboard-only host, configured with the service provider.</summary>
        /// <param name="setupAction">Configures the module's policies, node name, and forwarding.</param>
        /// <returns>The same builder for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="setupAction"/> is <see langword="null"/>.</exception>
        /// <exception cref="InvalidOperationException">A module with the id <c>messaging</c> is already added.</exception>
        public HeadlessDashboardSetupBuilder AddMessagingStandalone(
            Action<MessagingDashboardOptions, IServiceProvider> setupAction
        )
        {
            Argument.IsNotNull(setupAction);

            dashboard.Services.Configure<MessagingDashboardOptions, MessagingDashboardOptionsValidator>(setupAction);

            return _AddMessagingModule(dashboard, standalone: true);
        }

        /// <summary>
        /// Adds the Messaging module to a dashboard-only host, bound from <paramref name="configuration"/> with the
        /// keys <c>AddMessaging(IConfiguration)</c> reads.
        /// </summary>
        /// <param name="configuration">The configuration section to bind.</param>
        /// <returns>The same builder for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configuration"/> is <see langword="null"/>.</exception>
        /// <exception cref="InvalidOperationException">A module with the id <c>messaging</c> is already added.</exception>
        public HeadlessDashboardSetupBuilder AddMessagingStandalone(IConfiguration configuration)
        {
            Argument.IsNotNull(configuration);

            dashboard.Services.Configure<MessagingDashboardOptions, MessagingDashboardOptionsValidator>(configuration);

            return _AddMessagingModule(dashboard, standalone: true);
        }
    }

    private static HeadlessDashboardSetupBuilder _AddMessagingModule(
        HeadlessDashboardSetupBuilder dashboard,
        bool standalone = false
    )
    {
        var services = dashboard.Services;

        if (standalone)
        {
            // No store to read: every answer comes from a discovered node.
            services.AddSingleton<MessagingStandaloneMarker>();
            services.TryAddSingleton(provider =>
                MessagingStores.Empty(
                    provider.GetRequiredService<IOptions<MessagingDashboardOptions>>().Value,
                    provider.GetRequiredService<ILogger<MessagingStores>>()
                )
            );
        }
        else
        {
            // The module reads the stores AddHeadlessMessaging registers; without them nothing could serve a request,
            // so the host fails at start rather than on the first dashboard call.
            services.RequireRegisteredService<MessagingMarkerService>(
                "the Messaging dashboard module (AddMessaging)",
                "Call services.AddHeadlessMessaging(...) to register Headless Messaging."
            );

            services.TryAddSingleton(provider =>
                MessagingStores.Create(
                    provider.GetService<MessagingOutboxes>() ?? throw new InvalidOperationException(_MissingMessaging),
                    provider.GetRequiredService<IOptions<MessagingDashboardOptions>>().Value,
                    provider.GetRequiredService<ILogger<MessagingStores>>()
                )
            );
        }

        _AddForwarding(services);
        // The Kubernetes and Consul discovery providers cache their node lists here.
        services.TryAddSingleton<MessagingDashboardCache>();
        services.TryAddSingleton<MessagingMetricsEventListener>();
        services.TryAddSingleton(provider => new MessagingChangePublisher(
            provider.GetRequiredService<MessagingStores>(),
            provider.GetRequiredService<IDashboardEventPublisher>(),
            provider.GetRequiredService<ILogger<MessagingChangePublisher>>()
        ));
        services.TryAddSingleton(provider => new MessagingModule(
            provider.GetRequiredService<MessagingStores>(),
            provider.GetRequiredService<MessagingMetricsEventListener>(),
            provider.GetRequiredService<IOptions<MessagingDashboardOptions>>().Value,
            provider,
            provider.GetRequiredService<MessagingForwarding>(),
            provider.GetRequiredService<PeerChangeReader>()
        ));

        // Added first, so a second AddMessaging throws before it registers a second observer.
        var result = dashboard.AddModule<MessagingModule, MessagingDashboardOptions>(
            MessagingDashboardOptions.ModuleId
        );

        // The senders and consumers report each state write here, so this node's streams show it at once; other
        // nodes' writes arrive through the change reader.
        services.AddSingleton<IMessageStateObserver>(provider =>
            provider.GetRequiredService<MessagingChangePublisher>()
        );

        return result;
    }

    // Registered whether or not forwarding is on, so the options alone turn it on; every part is inert without a key
    // or a policy, and without a discovery provider.
    private static void _AddForwarding(IServiceCollection services)
    {
        services
            .AddAuthentication()
            .AddScheme<AuthenticationSchemeOptions, ForwardingAuthenticationHandler>(
                ForwardingProtocol.Scheme,
                static _ => { }
            );
        services
            .AddAuthorizationBuilder()
            .AddPolicy(
                ForwardingProtocol.Scheme,
                static policy =>
                    policy.AddAuthenticationSchemes(ForwardingProtocol.Scheme).RequireAssertion(_AdmitsPeerCall)
            );

        services.TryAddTransient<SharedKeyForwardingHandler>();
        services.AddHttpClient(ForwardingProtocol.ClientName).AddHttpMessageHandler<SharedKeyForwardingHandler>();

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton(provider => new PeerDirectory(
            provider,
            provider.GetRequiredService<IOptions<MessagingDashboardOptions>>().Value,
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<ILogger<PeerDirectory>>()
        ));
        services.TryAddSingleton(provider => new ForwardingClient(
            provider.GetRequiredService<IHttpClientFactory>(),
            provider.GetRequiredService<PeerDirectory>(),
            provider.GetRequiredService<IOptions<MessagingDashboardOptions>>().Value,
            provider.GetRequiredService<IOptions<HeadlessDashboardOptions>>(),
            provider.GetRequiredService<ILogger<ForwardingClient>>()
        ));
        services.TryAddSingleton(provider => new MessagingForwarding(
            provider.GetRequiredService<PeerDirectory>(),
            provider.GetRequiredService<ForwardingClient>(),
            provider.GetRequiredService<IOptions<MessagingDashboardOptions>>().Value,
            provider.GetRequiredService<TimeProvider>(),
            provider.GetService<MessagingStandaloneMarker>() is not null
        ));
        services.TryAddSingleton<PeerChangeReader>();
        services.TryAddSingleton<NodeRelayFilter>();
        services.AddStartupValidator<ForwardingStartupValidator>();
    }

    // The forwarding scheme admitted the node; a write also needs the operate access and a named operator, so the
    // ledger can say who ran it.
    private static bool _AdmitsPeerCall(AuthorizationHandlerContext context)
    {
        var user = context.User;

        if (!MessagingForwarding.IsForwarded(user))
        {
            return false;
        }

        var method = (context.Resource as HttpContext)?.Request.Method ?? string.Empty;

        if (HttpMethods.IsGet(method) || HttpMethods.IsHead(method))
        {
            return true;
        }

        return !MessagingForwarding.IsViewOnly(user) && !string.IsNullOrEmpty(user.FindFirst(ClaimTypes.Name)?.Value);
    }

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
            signInAvailable = config.Auth.SignInAvailable,
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
