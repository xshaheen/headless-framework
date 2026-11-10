// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Data.Common;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using Headless.Checks;
using Headless.Fencing;
using Headless.Sql;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Microsoft.EntityFrameworkCore;

/// <summary>Builds the fenced-row table descriptor of an entity from the context's relational model.</summary>
[PublicAPI]
public static class HeadlessFencingDbContextExtensions
{
    private static readonly ConditionalWeakTable<IModel, ConcurrentDictionary<DescriptorKey, object>> _Descriptors = [];

    /// <summary>
    /// Builds the descriptor that <see cref="FencedRows" /> transitions take for <typeparamref name="TEntity" />, with
    /// the table, schema, key, and lease column names the context's model maps.
    /// </summary>
    /// <typeparam name="TEntity">The entity type, mapped to one table.</typeparam>
    /// <typeparam name="TKey">The row key type: the primary key's CLR type, or the type <paramref name="key" /> maps.</typeparam>
    /// <param name="context">The context whose model maps the entity.</param>
    /// <param name="lease">The entity's lease property, the one <c>HasRowLease</c> maps.</param>
    /// <param name="dialect">The dialect of the context's database.</param>
    /// <param name="key">
    /// How the key maps to columns, or <see langword="null" /> to take it from a single-column primary key, bound in
    /// the type its value converter stores, if it has one.
    /// </param>
    /// <param name="generationSequence">
    /// The name, unquoted and in the table's schema, of a sequence that issues generations across the whole store, or
    /// <see langword="null" /> for each row to count its own.
    /// </param>
    /// <returns>
    /// The descriptor. One built without an explicit <paramref name="key" /> is cached per model, so calling this on
    /// every unit of work costs a dictionary lookup.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Run the transitions on the context's own connection and transaction, so a transition and the context's other
    /// writes commit or roll back together:
    /// <c>FencedRows.SettleAsync(table, context.Database.GetDbConnection(), context.Database.CurrentTransaction?.GetDbTransaction(), ...)</c>.
    /// Open the connection first when no transaction holds it open.
    /// </para>
    /// <para>
    /// The descriptor addresses the table in the schema EF Core maps it to: the table's own schema, else the model's
    /// default schema. A table with neither, and every table on SQLite, which has no schemas, is named without a
    /// schema, so the database resolves it as it resolves any unqualified name. <paramref name="generationSequence" />
    /// is addressed the same way.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="context" />, <paramref name="lease" />, or <paramref name="dialect" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="lease" /> is not a property of the entity, or <paramref name="dialect" /> does not match the
    /// context's database provider.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The entity is not in the model, its lease is not mapped with <c>HasRowLease</c>, it maps to no table or to more
    /// than one (entity splitting, table-per-type), its primary key has several columns and no <paramref name="key" />
    /// is given, or <typeparamref name="TKey" /> is not the primary key's type.
    /// </exception>
    /// <exception cref="NotSupportedException">
    /// No <paramref name="key" /> is given and the primary key stores a type other than <see cref="Guid" />,
    /// <see cref="long" />, <see cref="int" />, <see cref="short" />, <see cref="string" />, or a byte array.
    /// </exception>
    public static FencedRowTable<TKey> FencedRowTable<TEntity, TKey>(
        this DbContext context,
        Expression<Func<TEntity, RowLeaseSnapshot>> lease,
        ISqlDialect dialect,
        FencedRowKey<TKey>? key = null,
        string? generationSequence = null
    )
        where TEntity : class
        where TKey : notnull
    {
        Argument.IsNotNull(context);
        Argument.IsNotNull(lease);
        Argument.IsNotNull(dialect);

        if (
            lease.Body is not MemberExpression { Expression: ParameterExpression parameter } member
            || parameter != lease.Parameters[0]
        )
        {
            throw new ArgumentException(
                "The lease must be a property of the entity, as in 'e => e.Lease'.",
                nameof(lease)
            );
        }

        _EnsureDialectMatches(context, dialect);

        var model = context.Model;

        if (key is not null)
        {
            return _Build(model, typeof(TEntity), member.Member.Name, dialect, key, generationSequence);
        }

        var descriptors = _Descriptors.GetValue(model, static _ => new());

        return (FencedRowTable<TKey>)
            descriptors.GetOrAdd(
                new(typeof(TEntity), typeof(TKey), member.Member.Name, dialect, generationSequence),
                static (cacheKey, model) =>
                    _Build<TKey>(
                        model,
                        cacheKey.Entity,
                        cacheKey.Lease,
                        cacheKey.Dialect,
                        key: null,
                        cacheKey.GenerationSequence
                    ),
                model
            );
    }

    private static FencedRowTable<TKey> _Build<TKey>(
        IModel model,
        Type entity,
        string leaseName,
        ISqlDialect dialect,
        FencedRowKey<TKey>? key,
        string? generationSequence
    )
        where TKey : notnull
    {
        var entityType =
            model.FindEntityType(entity)
            ?? throw new InvalidOperationException($"The entity type '{entity.Name}' is not in the context's model.");

        var mappings = entityType.GetTableMappings().ToList();

        if (mappings.Count != 1)
        {
            throw new InvalidOperationException(
                mappings.Count == 0
                    ? $"The entity type '{entity.Name}' maps to no table."
                    : $"The entity type '{entity.Name}' maps to {mappings.Count} tables (entity splitting or table-per-type); a fenced row needs its key and lease columns in one table."
            );
        }

        var mapped = mappings[0].Table;
        var store = StoreObjectIdentifier.Table(mapped.Name, mapped.Schema);

        var complex = entityType.FindComplexProperty(leaseName);

        if (complex?.FindAnnotation(HeadlessFencingModelBuilderExtensions.RowLeaseAnnotation) is null)
        {
            throw new InvalidOperationException(
                $"The entity type '{entity.Name}' does not map '{leaseName}' as a lease. Call HasRowLease on its builder."
            );
        }

        string column(string property) =>
            complex.ComplexType.FindProperty(property)?.GetColumnName(store)
            ?? throw new InvalidOperationException(
                $"The lease '{leaseName}' of '{entity.Name}' maps no column for '{property}'. Call HasRowLease on its builder."
            );

        var columns = new FencedRowColumns
        {
            Generation = column(nameof(RowLeaseSnapshot.Generation)),
            State = column(nameof(RowLeaseSnapshot.State)),
            ExpiresAt = column(nameof(RowLeaseSnapshot.ExpiresAt)),
            Holder = column(nameof(RowLeaseSnapshot.Holder)),
            TakeoverCount = column(nameof(RowLeaseSnapshot.TakeoverCount)),
        };

        key ??= _PrimaryKey<TKey>(entityType, store);

        // EF Core's SQLite provider ignores schemas, so a schema in the model never names a SQLite table.
        var schema = string.Equals(dialect.DisplayName, "SQLite", StringComparison.Ordinal)
            ? null
            : mapped.Schema ?? model.GetDefaultSchema();

        return Headless.Fencing.FencedRowTable<TKey>.Create(
            dialect,
            schema,
            mapped.Name,
            key,
            columns,
            generationSequence
        );
    }

    private static FencedRowKey<TKey> _PrimaryKey<TKey>(IEntityType entityType, StoreObjectIdentifier store)
        where TKey : notnull
    {
        var primaryKey =
            entityType.FindPrimaryKey()
            ?? throw new InvalidOperationException(
                $"The entity type '{entityType.ClrType.Name}' has no primary key. Pass the key mapping explicitly."
            );

        if (primaryKey.Properties.Count != 1)
        {
            throw new InvalidOperationException(
                $"The primary key of '{entityType.ClrType.Name}' has {primaryKey.Properties.Count} columns. Pass a composite key mapping (FencedRowKey<TKey>.Composite) explicitly."
            );
        }

        var property = primaryKey.Properties[0];

        if (property.ClrType != typeof(TKey))
        {
            throw new InvalidOperationException(
                $"The primary key of '{entityType.ClrType.Name}' is a '{property.ClrType.Name}', not the key type '{typeof(TKey).Name}'."
            );
        }

        var name =
            property.GetColumnName(store)
            ?? throw new InvalidOperationException(
                $"The primary key of '{entityType.ClrType.Name}' maps no column in its table."
            );

        var converter = property.GetTypeMapping().Converter;
        var providerType = converter?.ProviderClrType ?? property.ClrType;
        var type = _ColumnType(providerType, property.GetMaxLength(), entityType.ClrType.Name);

        return converter is null
            ? FencedRowKey<TKey>.Scalar(name, type)
            : _Converted<TKey>(name, type, providerType, converter);
    }

    private static FencedRowKey<TKey> _Converted<TKey>(
        string column,
        SqlColumnType type,
        Type providerType,
        ValueConverter converter
    )
        where TKey : notnull
    {
        var read = _Readers[providerType];

        return FencedRowKey<TKey>.Composite(
            [new FencedRowKeyPart<TKey>(column, type, k => converter.ConvertToProvider(k))],
            (reader, ordinal) => (TKey)converter.ConvertFromProvider(read(reader, ordinal))!
        );
    }

    private static readonly Dictionary<Type, Func<DbDataReader, int, object>> _Readers = new()
    {
        [typeof(Guid)] = static (reader, ordinal) => reader.GetFieldValue<Guid>(ordinal),
        [typeof(long)] = static (reader, ordinal) => reader.GetFieldValue<long>(ordinal),
        [typeof(int)] = static (reader, ordinal) => reader.GetFieldValue<int>(ordinal),
        [typeof(short)] = static (reader, ordinal) => reader.GetFieldValue<short>(ordinal),
        [typeof(string)] = static (reader, ordinal) => reader.GetFieldValue<string>(ordinal),
        [typeof(byte[])] = static (reader, ordinal) => reader.GetFieldValue<byte[]>(ordinal),
    };

    private static SqlColumnType _ColumnType(Type providerType, int? maxLength, string entity)
    {
        if (providerType == typeof(Guid))
        {
            return SqlColumnType.Guid;
        }

        if (providerType == typeof(long))
        {
            return SqlColumnType.Int64;
        }

        if (providerType == typeof(int))
        {
            return SqlColumnType.Int32;
        }

        if (providerType == typeof(short))
        {
            return SqlColumnType.Int16;
        }

        if (providerType == typeof(string))
        {
            // Sized to the column so the comparison keeps its collation and index; -1 binds an unbounded column unsized.
            return SqlColumnType.Text(maxLength ?? -1);
        }

        if (providerType == typeof(byte[]))
        {
            return SqlColumnType.Binary;
        }

        throw new NotSupportedException(
            $"The primary key of '{entity}' is stored as '{providerType.Name}', which a fenced-row key cannot bind. Pass the key mapping explicitly."
        );
    }

    private static void _EnsureDialectMatches(DbContext context, ISqlDialect dialect)
    {
        var connection = context.Database.GetDbConnection();

        if (!dialect.ConnectionType.IsInstanceOfType(connection))
        {
            throw new ArgumentException(
                $"The {dialect.DisplayName} dialect runs on '{dialect.ConnectionType.Name}' connections, but the context uses the '{context.Database.ProviderName}' provider with a '{connection.GetType().Name}' connection.",
                nameof(dialect)
            );
        }
    }

    private readonly record struct DescriptorKey(
        Type Entity,
        Type Key,
        string Lease,
        ISqlDialect Dialect,
        string? GenerationSequence
    );
}
