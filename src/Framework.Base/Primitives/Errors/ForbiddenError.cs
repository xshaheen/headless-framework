// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Primitives;

/// <summary>
/// Operation not permitted for current user/context.
/// </summary>
[PublicAPI]
public sealed record ForbiddenError : ResultError
{
    public required string Reason { get; init; }

    public override string Code => "forbidden:access_denied";
    public override string Message => Reason;
}
