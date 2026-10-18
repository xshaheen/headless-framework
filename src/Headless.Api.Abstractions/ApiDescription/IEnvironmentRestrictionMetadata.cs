// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Hosting;

namespace Headless.Api;

/// <summary>
/// Endpoint metadata for an endpoint that serves requests only in some host environments and answers every other
/// request with a 404 problem-details response, as if it did not exist.
/// </summary>
/// <remarks>
/// API documentation reads it to leave the endpoint out of documents generated in an environment where it is not
/// available: Headless.OpenApi drops such an endpoint before it generates any path or schema.
/// </remarks>
[PublicAPI]
public interface IEnvironmentRestrictionMetadata
{
    /// <summary>Whether the endpoint serves requests in <paramref name="environment"/>.</summary>
    /// <param name="environment">The host environment the application runs in.</param>
    /// <returns>
    /// <see langword="true"/> when the endpoint runs in <paramref name="environment"/>; <see langword="false"/> when it
    /// answers 404 there.
    /// </returns>
    bool IsAvailableIn(IHostEnvironment environment);
}
