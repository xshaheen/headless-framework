// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Sql;

/// <summary>
/// A feature's storage objects, described once: the dialect renders them as idempotent DDL preceded by the
/// initialization locks, so replicas starting together create each object exactly once.
/// </summary>
/// <param name="Schema">The unquoted schema, already validated as an identifier.</param>
/// <param name="LockResourceParameter">
/// The parameter holding the feature's lock resource, keyed on the objects it creates, so two configurations that
/// point at different schemas never wait on each other.
/// </param>
/// <param name="Sequences">Sequences to create, as unqualified names in the dialect's naming convention.</param>
/// <param name="Tables">Tables to create, with their constraints and indexes.</param>
[PublicAPI]
public sealed record SqlSchemaScript(
    string Schema,
    string LockResourceParameter,
    IReadOnlyList<string> Sequences,
    IReadOnlyList<SqlTable> Tables
);

/// <summary>A table: columns, a primary key, check constraints, and filtered indexes.</summary>
/// <param name="Name">The unqualified name, in the dialect's naming convention.</param>
/// <param name="Columns">The columns, in order.</param>
/// <param name="PrimaryKeyName">The primary key constraint's name.</param>
/// <param name="PrimaryKey">The primary key's quoted columns; clustered where the engine clusters.</param>
/// <param name="Checks">Check constraints, as (name, condition over quoted columns) pairs.</param>
/// <param name="Indexes">Secondary indexes.</param>
[PublicAPI]
public sealed record SqlTable(
    string Name,
    IReadOnlyList<SqlColumn> Columns,
    string PrimaryKeyName,
    IReadOnlyList<string> PrimaryKey,
    IReadOnlyList<(string Name, string Condition)> Checks,
    IReadOnlyList<SqlIndex> Indexes
);

/// <summary>One column of a <see cref="SqlTable" />.</summary>
/// <param name="Name">The quoted column.</param>
/// <param name="Type">The portable type.</param>
/// <param name="Nullable">Whether the column accepts null.</param>
/// <param name="Default">A default expression, or <see langword="null" />.</param>
[PublicAPI]
public sealed record SqlColumn(string Name, SqlColumnType Type, bool Nullable = false, string? Default = null);

/// <summary>A secondary index, optionally filtered (partial).</summary>
/// <param name="Name">The unquoted index name, in the dialect's naming convention.</param>
/// <param name="Columns">The quoted indexed columns.</param>
/// <param name="Filter">The condition rows must meet to be indexed, or <see langword="null" />.</param>
[PublicAPI]
public sealed record SqlIndex(string Name, IReadOnlyList<string> Columns, string? Filter);
