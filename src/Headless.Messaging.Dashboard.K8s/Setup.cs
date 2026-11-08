// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Dashboard;

namespace Headless.Messaging.Dashboard.K8s;

/// <summary>Adds a dashboard-only Messaging host that finds its nodes in Kubernetes.</summary>
// ReSharper disable once InconsistentNaming
public static class SetupK8sDashboard
{
    extension(HeadlessDashboardSetupBuilder dashboard)
    {
        /// <summary>
        /// Adds the Messaging module to a dashboard-only host that shows the Messaging services Kubernetes lists, each
        /// through forwarding.
        /// </summary>
        /// <remarks>
        /// Set <see cref="MessagingDashboardOptions.Forwarding"/> with the same key or policy as the nodes, and
        /// <see cref="K8sDiscoveryOptions.Scheme"/> to <c>https</c> unless
        /// <see cref="MessagingForwardingOptions.AllowInsecurePeers"/> is set: the host fails to start without
        /// forwarding, and an <c>http</c> node is refused.
        /// </remarks>
        /// <param name="setupAction">Configures the module's policies, node name, and forwarding.</param>
        /// <param name="k8sAction">Configures how Kubernetes services are discovered.</param>
        /// <returns>The same builder for chaining.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="setupAction"/> or <paramref name="k8sAction"/> is <see langword="null"/>.
        /// </exception>
        /// <exception cref="InvalidOperationException">A module with the id <c>messaging</c> is already added.</exception>
        public HeadlessDashboardSetupBuilder AddMessagingStandalone(
            Action<MessagingDashboardOptions> setupAction,
            Action<K8sDiscoveryOptions> k8sAction
        )
        {
            Argument.IsNotNull(setupAction);
            Argument.IsNotNull(k8sAction);

            new K8sDiscoveryOptionsExtension(k8sAction).AddServices(dashboard.Services);

            return dashboard.AddMessagingStandalone(setupAction);
        }
    }
}
