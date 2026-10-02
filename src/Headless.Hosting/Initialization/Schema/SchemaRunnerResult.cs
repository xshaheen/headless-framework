// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Hosting.Initialization.Schema;

/// <summary>What one <see cref="SchemaRunner.ApplyAsync"/> run did.</summary>
[PublicAPI]
public sealed record SchemaRunnerResult
{
    /// <summary>The steps this run applied, in application order. Empty on a warm database.</summary>
    public required IReadOnlyList<SchemaAppliedStep> AppliedSteps { get; init; }

    /// <summary>
    /// History disagreements found for the applying features: a <see cref="SchemaMismatchKind.Checksum"/> change, which
    /// fails startup, or an <see cref="SchemaMismatchKind.Unknown"/> row, which is reported only.
    /// </summary>
    public required IReadOnlyList<SchemaMismatch> Mismatches { get; init; }

    /// <summary>
    /// How many times a step failed because another creator committed the same object first and was re-run. Runners
    /// never race each other under the per-database lock, so a non-zero value means a creator outside the runner.
    /// </summary>
    public required int AbsorbedRaces { get; init; }
}

/// <summary>One step a run applied.</summary>
/// <param name="Schema">The schema the step was applied in.</param>
/// <param name="Feature">The contributing feature.</param>
/// <param name="Version">The step's version.</param>
[PublicAPI]
public sealed record SchemaAppliedStep(string Schema, string Feature, string Version);

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

/// <summary>Thrown when the schema runner cannot open a connection, take its lock, apply or record a step, or when startup finds a fatal history mismatch.</summary>
[PublicAPI]
public sealed class SchemaRunnerException(string message, Exception? innerException = null)
    : InvalidOperationException(message, innerException)
{
    /// <summary>The mismatches that failed startup, when that is the cause; otherwise empty.</summary>
    public IReadOnlyList<SchemaMismatch> Mismatches { get; init; } = [];
}
