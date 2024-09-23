// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Framework.Kernel.BuildingBlocks;

public static class PlatformEnvironmentVariables
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
