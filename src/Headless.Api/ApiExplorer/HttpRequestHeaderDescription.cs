// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Api;

/// <summary>A request header an endpoint reads, as part of <see cref="HttpContractMetadata"/>.</summary>
/// <param name="name">The header name. Must not be empty or white space.</param>
/// <param name="required">Whether the endpoint rejects a request that omits the header.</param>
/// <param name="description">What the header carries and how the endpoint uses it. Must not be empty or white space.</param>
[PublicAPI]
public sealed class HttpRequestHeaderDescription(string name, bool required, string description)
{
    /// <summary>The header name.</summary>
    public string Name { get; } = Argument.IsNotNullOrWhiteSpace(name);

    /// <summary>Whether the endpoint rejects a request that omits the header.</summary>
    public bool Required { get; } = required;

    /// <summary>What the header carries and how the endpoint uses it.</summary>
    public string Description { get; } = Argument.IsNotNullOrWhiteSpace(description);
}
