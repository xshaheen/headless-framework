// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Constants;

/// <summary>Well-known API version strings used across Headless API versioning.</summary>
/// <remarks>
/// Pass these constants to API-versioning attributes (e.g. <c>[ApiVersion(HeadlessApiVersions.V1)]</c>)
/// and to route constraints to avoid hard-coding literal version strings.
/// </remarks>
[PublicAPI]
public static class HeadlessApiVersions
{
    /// <summary>API version 1.0.</summary>
    public const string V1 = "1.0";

    /// <summary>API version 2.0.</summary>
    public const string V2 = "2.0";

    /// <summary>API version 3.0.</summary>
    public const string V3 = "3.0";
}
