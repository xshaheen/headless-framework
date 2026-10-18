// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Api;

/// <summary>A response an endpoint may return, as part of <see cref="HttpContractMetadata"/>.</summary>
/// <param name="statusCode">The HTTP status code, from 100 to 599.</param>
/// <param name="description">When the endpoint returns the response. Must not be empty or white space.</param>
/// <remarks>
/// It carries no body type. An error status describes a problem-details response, which documentation renders with
/// the body the Headless problem-details writer produces for that status.
/// </remarks>
[PublicAPI]
public sealed class HttpResponseDescription(int statusCode, string description)
{
    /// <summary>The HTTP status code.</summary>
    public int StatusCode { get; } = Argument.IsInclusiveBetween(statusCode, 100, 599);

    /// <summary>When the endpoint returns the response.</summary>
    public string Description { get; } = Argument.IsNotNullOrWhiteSpace(description);
}
