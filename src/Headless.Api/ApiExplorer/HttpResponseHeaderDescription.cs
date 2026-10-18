// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Api;

/// <summary>
/// A header an endpoint's responses may carry, as part of <see cref="HttpContractMetadata"/>, on every response whose
/// status code is in an inclusive range.
/// </summary>
/// <param name="name">The header name. Must not be empty or white space.</param>
/// <param name="minStatusCode">The lowest status code of the responses that carry the header, from 100 to 599.</param>
/// <param name="maxStatusCode">
/// The highest status code of the responses that carry the header, from <paramref name="minStatusCode"/> to 599.
/// </param>
/// <param name="description">What the header carries and when the endpoint sends it. Must not be empty or white space.</param>
/// <remarks>
/// A range rather than a list lets a feature describe a header on every success response (200 to 299) without knowing
/// which success statuses the handler returns. The header applies only to responses the endpoint documents.
/// </remarks>
[PublicAPI]
public sealed class HttpResponseHeaderDescription(string name, int minStatusCode, int maxStatusCode, string description)
{
    /// <summary>Initializes a header carried by responses with one status code.</summary>
    /// <param name="name">The header name.</param>
    /// <param name="statusCode">The status code of the responses that carry the header, from 100 to 599.</param>
    /// <param name="description">What the header carries and when the endpoint sends it.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="statusCode"/> is outside 100 to 599.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> or <paramref name="description"/> is empty or white space.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> or <paramref name="description"/> is <see langword="null"/>.</exception>
    public HttpResponseHeaderDescription(string name, int statusCode, string description)
        : this(name, statusCode, statusCode, description) { }

    /// <summary>The header name.</summary>
    public string Name { get; } = Argument.IsNotNullOrWhiteSpace(name);

    /// <summary>The lowest status code of the responses that carry the header.</summary>
    public int MinStatusCode { get; } = Argument.IsInclusiveBetween(minStatusCode, 100, 599);

    /// <summary>The highest status code of the responses that carry the header.</summary>
    public int MaxStatusCode { get; } = Argument.IsInclusiveBetween(maxStatusCode, minStatusCode, 599);

    /// <summary>What the header carries and when the endpoint sends it.</summary>
    public string Description { get; } = Argument.IsNotNullOrWhiteSpace(description);

    /// <summary>Whether every response in the range carries the header. Default is <see langword="false"/>.</summary>
    public bool Required { get; init; }
}
