// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Api;

/// <summary>A request header an endpoint reads, as part of <see cref="HttpContractMetadata"/>.</summary>
[PublicAPI]
public sealed record HttpRequestHeaderDescription
{
    /// <summary>Initializes a request header description.</summary>
    /// <param name="name">The header name. Must not be empty or white space.</param>
    /// <param name="required">Whether the endpoint rejects a request that omits the header.</param>
    /// <param name="description">What the header carries and how the endpoint uses it. Must not be empty or white space.</param>
    /// <exception cref="ArgumentException"><paramref name="name"/> or <paramref name="description"/> is empty or white space.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> or <paramref name="description"/> is <see langword="null"/>.</exception>
    public HttpRequestHeaderDescription(string name, bool required, string description)
    {
        Name = Argument.IsNotNullOrWhiteSpace(name);
        Required = required;
        Description = Argument.IsNotNullOrWhiteSpace(description);
    }

    /// <summary>The header name.</summary>
    public string Name { get; }

    /// <summary>Whether the endpoint rejects a request that omits the header.</summary>
    public bool Required { get; }

    /// <summary>What the header carries and how the endpoint uses it.</summary>
    public string Description { get; }
}
