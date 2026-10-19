// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Api;

/// <summary>A response an endpoint may return, as part of <see cref="HttpContractMetadata"/>.</summary>
/// <remarks>
/// It carries no body type. An error status describes a problem-details response, which documentation renders with
/// the body the Headless problem-details writer produces for that status.
/// </remarks>
[PublicAPI]
public sealed record HttpResponseDescription
{
    /// <summary>Initializes a response description.</summary>
    /// <param name="statusCode">The HTTP status code, from 100 to 599.</param>
    /// <param name="description">When the endpoint returns the response. Must not be empty or white space.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="statusCode"/> is outside 100 to 599.</exception>
    /// <exception cref="ArgumentException"><paramref name="description"/> is empty or white space.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="description"/> is <see langword="null"/>.</exception>
    public HttpResponseDescription(int statusCode, string description)
    {
        StatusCode = Argument.IsInclusiveBetween(statusCode, 100, 599);
        Description = Argument.IsNotNullOrWhiteSpace(description);
    }

    /// <summary>The HTTP status code.</summary>
    public int StatusCode { get; }

    /// <summary>When the endpoint returns the response.</summary>
    public string Description { get; }
}
