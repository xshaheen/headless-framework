// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Primitives;

/// <summary>
/// Business rule conflict (duplicate, invalid state, etc.).
/// </summary>
[PublicAPI]
public sealed record ConflictError(string Code, string Message) : ResultError
{
    public override string Code { get; } = Code;
    public override string Message { get; } = Message;
}
