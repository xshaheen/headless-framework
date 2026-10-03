// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Checks;

namespace Headless.Hosting.Initialization.Schema;

/// <summary>
/// One idempotent DDL step a feature contributes to the Headless schema runner. The runner applies a step at most
/// once per schema, records it in the history table with a checksum of its SQL, and never re-runs a recorded step.
/// </summary>
/// <remarks>
/// A step's SQL must be idempotent (<c>IF NOT EXISTS</c> on every create, catalog checks around every alter): the
/// runner re-runs a step once after absorbing a concurrent-DDL race, and the exported deploy script is safe to run
/// twice only because every step is. A step must not create the schema; the runner creates it with the history table.
/// </remarks>
[PublicAPI]
public sealed record SchemaStep
{
    /// <summary>Creates a step.</summary>
    /// <param name="version">
    /// The step's version, unique within its feature. Never renumber or edit a shipped step: add a new version, because
    /// the recorded checksum pins the applied SQL.
    /// </param>
    /// <param name="description">A human-readable summary recorded in the history table.</param>
    /// <param name="sql">The idempotent DDL batch.</param>
    /// <exception cref="ArgumentException">Any argument is null, empty, or whitespace.</exception>
    public SchemaStep(string version, string description, string sql)
    {
        Version = Argument.IsNotNullOrWhiteSpace(version);
        Description = Argument.IsNotNullOrWhiteSpace(description);
        Sql = Argument.IsNotNullOrWhiteSpace(sql);
    }

    /// <summary>The step's version, unique within its feature.</summary>
    public string Version { get; }

    /// <summary>The step's description, recorded in the history table.</summary>
    public string Description { get; }

    /// <summary>The idempotent DDL batch.</summary>
    public string Sql { get; }
}
