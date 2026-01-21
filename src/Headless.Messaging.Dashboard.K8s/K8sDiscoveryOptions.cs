// Copyright (c) Mahmoud Shaheen. All rights reserved.

using k8s;

namespace Headless.Messaging.Dashboard.K8s;

// ReSharper disable once InconsistentNaming
/// <summary>
/// Represents all the option you can use to configure the k8s discovery.
/// </summary>
public class K8sDiscoveryOptions
{
    public KubernetesClientConfiguration K8SClientConfig { get; set; } =
        KubernetesClientConfiguration.BuildDefaultConfig();

    /// <summary>
    /// If this is set to TRUE will make all nodes hidden by default. Only kubernetes services
    /// with label "headless.messaging.visibility:show" will be listed in the nodes section.
    /// </summary>
    public bool ShowOnlyExplicitVisibleNodes { get; set; } = true;
}
