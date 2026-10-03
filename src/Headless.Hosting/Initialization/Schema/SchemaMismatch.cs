// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Hosting.Initialization.Schema;

/// <summary>The kind of disagreement between the history table and the registered steps.</summary>
[PublicAPI]
public enum SchemaMismatchKind
{
    /// <summary>A registered step has no history row: the database lacks it. Fails verify mode.</summary>
    Missing = 0,

    /// <summary>
    /// A history row names a registered feature but a version this code does not register, as when an older replica
    /// starts after a newer one applied a later step during a rolling deploy. Reported, never fatal.
    /// </summary>
    Unknown = 1,

    /// <summary>A history row's checksum differs from its registered step: the step's SQL changed after it shipped. Fails both modes.</summary>
    Checksum = 2,
}

/// <summary>One disagreement between the history table and the registered steps.</summary>
/// <param name="Schema">The schema whose history disagrees.</param>
/// <param name="Feature">The feature the row or step belongs to.</param>
/// <param name="Version">The step version.</param>
/// <param name="Kind">The kind of disagreement.</param>
/// <param name="ExpectedChecksum">The registered step's checksum, when a step is registered.</param>
/// <param name="ActualChecksum">The recorded checksum, when a row exists.</param>
[PublicAPI]
public sealed record SchemaMismatch(
    string Schema,
    string Feature,
    string Version,
    SchemaMismatchKind Kind,
    string? ExpectedChecksum,
    string? ActualChecksum
)
{
    /// <inheritdoc />
    public override string ToString()
    {
        return $"{Kind} {Schema}/{Feature}/{Version}";
    }
}
