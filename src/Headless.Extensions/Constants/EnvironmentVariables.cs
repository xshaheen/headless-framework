// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Constants;

[PublicAPI]
public static class EnvironmentVariables
{
    public const string Hostname = "HOSTNAME";
    public const string Environment = "Environment";

    public static class Dotnet
    {
        // ReSharper disable once MemberHidesStaticFromOuterClass
        public const string Environment = "ASPNETCORE_ENVIRONMENT";
        public const string DotNetRunningInContainer = "DOTNET_RUNNING_IN_CONTAINER";
    }

    public static class Kubernetes
    {
        public const string KubernetesVariablePrefix = "KUBERNETES";
    }
}
