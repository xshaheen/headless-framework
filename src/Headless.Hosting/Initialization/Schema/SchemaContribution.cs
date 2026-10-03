// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Checks;

namespace Headless.Hosting.Initialization.Schema;

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
    /// <exception cref="ArgumentNullException"><paramref name="dialect"/>, <paramref name="createConnection"/>, or <paramref name="steps"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="feature"/> or <paramref name="schema"/> is null or whitespace, <paramref name="steps"/> is
    /// empty, or two steps share a version.
    /// </exception>
    public SchemaContribution(
        string feature,
        ISchemaDialect dialect,
        Func<DbConnection> createConnection,
        string schema,
        IReadOnlyList<SchemaStep> steps,
        bool applyOnStartup = true,
        bool exportOnly = false
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
