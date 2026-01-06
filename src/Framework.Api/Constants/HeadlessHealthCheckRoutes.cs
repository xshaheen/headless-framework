// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Framework.Constants;

[PublicAPI]
public static class HeadlessHealthCheckRoutes
{
    public const string Health = "/health";
    public const string Alive = "/alive";
}
