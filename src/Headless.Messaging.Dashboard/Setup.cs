// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Claims;
using Headless.Checks;
using Headless.Dashboard;
using Headless.Hosting;
using Headless.Messaging.Internal;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.Dashboard;

public static class SetupMessagingDashboard
{
    private const string _MissingMessaging =
        "The Messaging dashboard module (AddMessaging) needs Headless Messaging. Call "
        + "services.AddHeadlessMessaging(...) to register it.";

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
        /// without calling <c>AddHeadlessMessaging</c> fails to start with a message naming that call.
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
}
