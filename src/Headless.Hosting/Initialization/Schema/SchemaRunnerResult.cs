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
