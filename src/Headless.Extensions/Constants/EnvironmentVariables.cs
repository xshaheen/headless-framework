// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Constants;

/// <summary>
/// Names of operating-system environment variables the framework reads for host, runtime, and
/// orchestration detection (current environment, container/Kubernetes probes, hostname).
/// </summary>
[PublicAPI]
public static class EnvironmentVariables
{
    /// <summary>The host machine / container name (<c>HOSTNAME</c>), set by the OS or container runtime.</summary>
    public const string Hostname = "HOSTNAME";

    /// <summary>Generic <c>Environment</c> variable name; see <see cref="Dotnet.Environment"/> for the ASP.NET Core-specific name.</summary>
    public const string Environment = "Environment";

    /// <summary>.NET / ASP.NET Core runtime environment variables.</summary>
    public static class Dotnet
    {
        /// <summary>ASP.NET Core's environment-name variable (<c>ASPNETCORE_ENVIRONMENT</c>), e.g. <c>Development</c> or <c>Production</c>.</summary>
        // ReSharper disable once MemberHidesStaticFromOuterClass
        public const string Environment = "ASPNETCORE_ENVIRONMENT";

        /// <summary>Set to <c>true</c> by official .NET container images to indicate the process runs inside a container (<c>DOTNET_RUNNING_IN_CONTAINER</c>).</summary>
        public const string DotNetRunningInContainer = "DOTNET_RUNNING_IN_CONTAINER";
    }

    /// <summary>Kubernetes-injected environment variables.</summary>
    public static class Kubernetes
    {
        /// <summary>Prefix shared by the variables Kubernetes injects into pods (for example <c>KUBERNETES_SERVICE_HOST</c>); presence of any <c>KUBERNETES*</c> variable signals the process runs in a cluster.</summary>
        public const string KubernetesVariablePrefix = "KUBERNETES";
    }
}
