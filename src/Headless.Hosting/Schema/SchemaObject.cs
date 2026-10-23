// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Hosting.Schema;

/// <summary>
/// A database object a schema contribution creates and owns: what <c>resources purge</c> empties and
/// <c>resources delete</c> drops. Indexes, constraints, and triggers belong to their table and go with it.
/// </summary>
[PublicAPI]
public sealed record SchemaObject
{
    /// <summary>Creates an object declaration.</summary>
    /// <param name="kind">The kind of object.</param>
    /// <param name="name">
    /// The unquoted name within the contribution's schema. On SQLite, where the schema is a table-name prefix, the name
    /// carries the prefix, as <see cref="SchemaContribution.HostStateTables"/> does.
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="name"/> is null or whitespace.</exception>
    public SchemaObject(SchemaObjectKind kind, string name)
    {
        Kind = kind;
        Name = Argument.IsNotNullOrWhiteSpace(name);
    }

    /// <summary>The kind of object.</summary>
    public SchemaObjectKind Kind { get; }

    /// <summary>The unquoted name within the contribution's schema.</summary>
    public string Name { get; }

    /// <summary>Declares a table.</summary>
    /// <param name="name">The unquoted table name.</param>
    /// <returns>The declaration.</returns>
    public static SchemaObject Table(string name) => new(SchemaObjectKind.Table, name);

    /// <summary>Declares a sequence.</summary>
    /// <param name="name">The unquoted sequence name.</param>
    /// <returns>The declaration.</returns>
    public static SchemaObject Sequence(string name) => new(SchemaObjectKind.Sequence, name);
}

/// <summary>The kind of a <see cref="SchemaObject"/>.</summary>
[PublicAPI]
public enum SchemaObjectKind
{
    /// <summary>A table. Clear deletes its rows; teardown drops it with its indexes, constraints, and triggers.</summary>
    Table = 0,

    /// <summary>
    /// A sequence. Clear keeps it, so a generation it issues never repeats a value already handed out; teardown drops
    /// it. On SQLite, which has no sequences, it is the one-row table that stands in for one, and teardown drops that
    /// table.
    /// </summary>
    Sequence = 1,
}
