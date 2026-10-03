// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Primitives;

/// <summary>
/// The requested resource was not found.
/// </summary>
[PublicAPI]
public sealed record NotFoundError : ApiResultError
{
    /// <summary>The logical name of the entity that could not be found.</summary>
    public required string Entity { get; init; }

    /// <summary>The key or identifier used to look up the entity.</summary>
    public required string Key { get; init; }

    /// <inheritdoc/>
    // Computed (not field-backed): a `field` backing store would participate in the record's
    // auto-generated equality and flip two logically-equal errors to unequal once read. See ValidationError.Metadata.
    public override string Code => $"notfound:{Entity.ToLowerInvariant()}";

    /// <inheritdoc/>
    public override string Message => $"{Entity} with key '{Key}' was not found.";

    /// <inheritdoc/>
    public override IReadOnlyDictionary<string, object?> Metadata =>
        new Dictionary<string, object?>(StringComparer.Ordinal) { ["entity"] = Entity, ["key"] = Key };
}
