// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Checks;
using Headless.Sql;

#pragma warning disable CA1000 // The factories name the key type, which no argument carries for inference to find.
namespace Headless.Fencing;

/// <summary>
/// How a key type maps to a table's key columns: binds a key's parts as parameters, and reads a key back from a claim's
/// result set.
/// </summary>
/// <typeparam name="TKey">The row's key type: a scalar such as <see cref="Guid" /> or <see cref="long" />, or a record for a composite key.</typeparam>
[PublicAPI]
public sealed class FencedRowKey<TKey>
    where TKey : notnull
{
    private readonly Func<DbDataReader, int, TKey> _read;

    private FencedRowKey(IReadOnlyList<FencedRowKeyPart<TKey>> parts, Func<DbDataReader, int, TKey> read)
    {
        Parts = parts;
        _read = read;
    }

    /// <summary>Gets the key's columns, in the order they are bound and read.</summary>
    public IReadOnlyList<FencedRowKeyPart<TKey>> Parts { get; }

    /// <summary>
    /// A key stored in one column, read back with <see cref="DbDataReader.GetFieldValue{T}(int)" />.
    /// </summary>
    /// <param name="column">The key column's name, unquoted.</param>
    /// <param name="type">The key column's storage type.</param>
    /// <returns>The key mapping.</returns>
    /// <exception cref="ArgumentException"><paramref name="column" /> is null, empty, or white space.</exception>
    public static FencedRowKey<TKey> Scalar(string column, SqlColumnType type)
    {
        Argument.IsNotNullOrWhiteSpace(column);

        return new(
            [new FencedRowKeyPart<TKey>(column, type, static key => key)],
            static (reader, ordinal) => reader.GetFieldValue<TKey>(ordinal)
        );
    }

    /// <summary>A key stored in several columns.</summary>
    /// <param name="parts">The key's columns, in order.</param>
    /// <param name="read">
    /// Reads a key from the result set, its first column at the given ordinal and the rest after it in
    /// <paramref name="parts" /> order.
    /// </param>
    /// <returns>The key mapping.</returns>
    /// <exception cref="ArgumentException"><paramref name="parts" /> is empty, a column is blank, or a column repeats.</exception>
    public static FencedRowKey<TKey> Composite(
        IReadOnlyList<FencedRowKeyPart<TKey>> parts,
        Func<DbDataReader, int, TKey> read
    )
    {
        Argument.IsNotNullOrEmpty(parts);
        Argument.IsNotNull(read);

        foreach (var part in parts)
        {
            Argument.IsNotNull(part);
            Argument.IsNotNullOrWhiteSpace(part.Column, paramName: nameof(parts));
            Argument.IsNotNull(part.Value, paramName: nameof(parts));
        }

        if (new HashSet<string>(parts.Select(static p => p.Column), StringComparer.Ordinal).Count != parts.Count)
        {
            throw new ArgumentException("A key column appears more than once.", nameof(parts));
        }

        return new([.. parts], read);
    }

    internal TKey Read(DbDataReader reader, int ordinal) => _read(reader, ordinal);
}

/// <summary>One column of a row's key: its unquoted name, its storage type, and how to take its value from a key.</summary>
/// <param name="Column">The column's name, unquoted.</param>
/// <param name="Type">The column's storage type, so the bound value compares with the column's collation and index.</param>
/// <param name="Value">Takes this column's value from a key.</param>
/// <typeparam name="TKey">The row's key type.</typeparam>
[PublicAPI]
public sealed record FencedRowKeyPart<TKey>(string Column, SqlColumnType Type, Func<TKey, object?> Value)
    where TKey : notnull;
