// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Primitives;

/// <summary>
/// Caller is not authenticated.
/// </summary>
[PublicAPI]
public sealed record UnauthorizedError : ResultError
{
    /// <summary>
    /// Cached singleton instance for common case.
    /// </summary>
    public static readonly UnauthorizedError Instance = new();

    public override string Code => "unauthorized";
    public override string Message => "Authentication required.";
}
