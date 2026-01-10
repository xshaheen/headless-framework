// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Primitives;

/// <summary>
/// The requested resource was not found.
/// </summary>
[PublicAPI]
public sealed record NotFoundError : ResultError
{
    private string? _code;
    private IReadOnlyDictionary<string, object?>? _metadata;

    public required string Entity { get; init; }
    public required string Key { get; init; }

    public override string Code => _code ??= $"notfound:{Entity.ToLowerInvariant()}";
    public override string Message => $"{Entity} with key '{Key}' was not found.";

    public override IReadOnlyDictionary<string, object?> Metadata =>
        _metadata ??= new Dictionary<string, object?>(StringComparer.Ordinal) { ["entity"] = Entity, ["key"] = Key };
}
