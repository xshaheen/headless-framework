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

/// <summary>
/// The ordered steps one feature contributes to one schema in one database, plus how to reach that database. The
/// runner groups contributions by database, so every feature sharing a connection is applied under one lock in one
/// pass.
/// </summary>
[PublicAPI]
public sealed record SchemaContribution
{
    /// <summary>Creates a contribution.</summary>
    /// <param name="feature">
    /// The feature's stable name (<c>"Sequences"</c>), recorded in the history table. Renaming it orphans its history.
    /// </param>
    /// <param name="dialect">The database dialect that owns locking, history DDL, and error classification.</param>
    /// <param name="createConnection">
    /// Creates an unopened connection to the feature's database. The runner derives the database identity from it
    /// before opening, opens it, and disposes it.
    /// </param>
    /// <param name="schema">The already-validated schema that holds the feature's objects and the history table.</param>
    /// <param name="steps">The feature's steps, applied in list order.</param>
    /// <param name="applyOnStartup">
    /// <see langword="false"/> keeps the steps registered, so verify mode and script export still describe them, but
    /// the runner never applies them: the feature's <c>InitializeOnStartup = false</c> contract.
    /// </param>
    /// <param name="exportOnly">
    /// <see langword="true"/> limits the contribution to <see cref="SchemaRunner.ExportScript"/>: the runner neither
    /// applies nor verifies it. For a feature that runs its database's steps with a runner of its own, so that one
    /// database being unreachable cannot fail the host's startup, and still belongs in the host's deploy script.
    /// </param>
    /// <param name="hostStateTables">
    /// The unquoted names, within <paramref name="schema"/>, of the feature's tables whose rows are host state rather
    /// than application data: rows the feature writes at startup from code (definitions) or keeps live while the host
    /// runs (cluster membership). A tool that clears application data between tests must keep them, because the
    /// running host never writes them again. Omit for a feature whose tables hold only application data.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="dialect"/>, <paramref name="createConnection"/>, or <paramref name="steps"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="feature"/> or <paramref name="schema"/> is null or whitespace, <paramref name="steps"/> is
    /// empty, two steps share a version, or <paramref name="hostStateTables"/> holds a null or whitespace name.
    /// </exception>
    public SchemaContribution(
        string feature,
        ISchemaDialect dialect,
        Func<DbConnection> createConnection,
        string schema,
        IReadOnlyList<SchemaStep> steps,
        bool applyOnStartup = true,
        bool exportOnly = false,
        IReadOnlyList<string>? hostStateTables = null
    )
    {
        Feature = Argument.IsNotNullOrWhiteSpace(feature);
        Dialect = Argument.IsNotNull(dialect);
        CreateConnection = Argument.IsNotNull(createConnection);
        Schema = Argument.IsNotNullOrWhiteSpace(schema);
        Argument.IsNotNull(steps);
        Argument.IsTrue(steps.Count > 0, "A schema contribution needs at least one step.", nameof(steps));
        Argument.HasNoDuplicates(
            steps.Select(s => s.Version).ToList(),
            StringComparer.Ordinal,
            "A schema contribution cannot repeat a step version.",
            nameof(steps)
        );
        Steps = steps;
        ApplyOnStartup = applyOnStartup;
        ExportOnly = exportOnly;
        HostStateTables = hostStateTables ?? [];
        Argument.IsTrue(
            HostStateTables.All(static name => !string.IsNullOrWhiteSpace(name)),
            "A host-state table name cannot be null or whitespace.",
            nameof(hostStateTables)
        );
    }

    /// <summary>The feature's stable name.</summary>
    public string Feature { get; }

    /// <summary>The database dialect.</summary>
    public ISchemaDialect Dialect { get; }

    /// <summary>Creates an unopened connection to the feature's database.</summary>
    public Func<DbConnection> CreateConnection { get; }

    /// <summary>The schema that holds the feature's objects and the history table.</summary>
    public string Schema { get; }

    /// <summary>The feature's steps, in application order.</summary>
    public IReadOnlyList<SchemaStep> Steps { get; }

    /// <summary>Whether the runner applies the steps at startup.</summary>
    public bool ApplyOnStartup { get; }

    /// <summary>Whether the runner only exports the steps, and never applies or verifies them.</summary>
    public bool ExportOnly { get; }

    /// <summary>
    /// The unquoted names, within <see cref="Schema"/>, of the tables whose rows are host state written at startup or
    /// kept live by the running host, so a test data reset keeps them. Empty when every table holds application data.
    /// </summary>
    public IReadOnlyList<string> HostStateTables { get; }

    /// <summary>
    /// Returns the history identity of a feature whose object names are configurable: <paramref name="feature"/>
    /// alone when every name is its default, otherwise the feature followed by each non-default name
    /// (<c>Sequences:counters</c>).
    /// </summary>
    /// <remarks>
    /// Two hosts that share a schema but name the feature's objects differently create different objects with
    /// different SQL. Under one identity the second host would read the first host's row as a checksum change and never
    /// create its own objects; the names in the identity keep their histories apart.
    /// </remarks>
    /// <param name="feature">The feature's stable name.</param>
    /// <param name="names">Each configurable name as (configured, default); a null configured name means the default.</param>
    /// <returns>The history identity.</returns>
    /// <exception cref="ArgumentException"><paramref name="feature"/> is null or whitespace.</exception>
    public static string FeatureId(string feature, params ReadOnlySpan<(string? Configured, string Default)> names)
    {
        Argument.IsNotNullOrWhiteSpace(feature);

        var id = feature;

        foreach (var (configured, @default) in names)
        {
            if (configured is not null && !string.Equals(configured, @default, StringComparison.Ordinal))
            {
                id += ":" + configured;
            }
        }

        return id;
    }
}
