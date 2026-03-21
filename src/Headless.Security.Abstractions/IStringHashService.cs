// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Abstractions;

/// <summary>Creates deterministic hashes for string values.</summary>
[PublicAPI]
public interface IStringHashService
{
    /// <summary>Creates a hash for the specified value.</summary>
    /// <param name="value">The value to hash.</param>
    /// <param name="salt">
    /// An optional salt override. Uses the configured default salt when omitted, or an empty salt when no default is
    /// configured.
    /// </param>
    /// <returns>The computed hash encoded as Base64.</returns>
    string Create(string value, string? salt = null);
}
