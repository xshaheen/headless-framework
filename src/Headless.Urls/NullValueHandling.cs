// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Urls;

/// <summary>
/// Describes how to handle null values in query parameters.
/// </summary>
/// <remarks>
/// The backing values are part of the public contract and must remain stable across versions; assign new members
/// explicit values rather than reordering existing ones.
/// </remarks>
[PublicAPI]
public enum NullValueHandling
{
    /// <summary>
    /// Set as name without value in query string.
    /// </summary>
    NameOnly = 0,

    /// <summary>
    /// Don't add to query string, remove any existing value.
    /// </summary>
    Remove = 1,

    /// <summary>
    /// Don't add to query string, but leave any existing value unchanged.
    /// </summary>
    Ignore = 2,
}
