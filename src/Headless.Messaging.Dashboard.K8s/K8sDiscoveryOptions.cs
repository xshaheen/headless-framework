// Copyright (c) Mahmoud Shaheen. All rights reserved.

using k8s;

namespace Headless.Messaging.Dashboard.K8s;

// ReSharper disable once InconsistentNaming
/// <summary>
/// Configuration for Kubernetes-based node discovery used by the Messaging Dashboard.
/// Controls which Kubernetes services are listed as peer nodes and how the Kubernetes
/// client connects to the cluster.
/// </summary>
public class K8sDiscoveryOptions
{
    /// <summary>
    /// Kubernetes client configuration used to connect to the cluster API.
    /// Defaults to the ambient in-cluster or kubeconfig-file configuration
    /// resolved by <c>KubernetesClientConfiguration.BuildDefaultConfig()</c>.
    /// </summary>
    public KubernetesClientConfiguration K8SClientConfig { get; set; } =
        KubernetesClientConfiguration.BuildDefaultConfig();

    /// <summary>
    /// If this is set to TRUE will make all nodes hidden by default. Only kubernetes services
    /// with label "headless.messaging.visibility:show" will be listed in the nodes section.
    /// </summary>
    public bool ShowOnlyExplicitVisibleNodes { get; set; } = true;
}
