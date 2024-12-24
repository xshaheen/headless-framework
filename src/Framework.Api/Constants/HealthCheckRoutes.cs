// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Framework.Constants;

[PublicAPI]
public static class HealthCheckRoutes
{
    public const string StatusHealthCheckPath = "/status";
    public const string SelfHealthCheckPath = "/status/self";
}
