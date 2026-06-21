// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Constants;

/// <summary>Named CORS policy identifiers used by the Headless framework.</summary>
/// <remarks>
/// Reference these constants when registering CORS middleware (e.g. <c>app.UseCors(HeadlessCorsConstants.RestrictedCors)</c>)
/// and when calling <c>services.AddCors()</c> policy configuration, so policy names stay in sync
/// across the pipeline.
/// </remarks>
[PublicAPI]
public static class HeadlessCorsConstants
{
    /// <summary>Name of the CORS policy that restricts access to a configured list of allowed origins.</summary>
    public const string RestrictedCors = "_origins";

    /// <summary>Name of the CORS policy that allows requests from any origin.</summary>
    public const string AllowAnyCors = "_any_origins";
}
