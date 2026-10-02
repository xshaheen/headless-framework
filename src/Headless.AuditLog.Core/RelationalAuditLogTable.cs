// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Sql;

namespace Headless.AuditLog;

/// <summary>
/// Binds an instant as a parameter typed like the <c>CreatedAt</c> column, so a comparison never converts the column
/// and keeps its index usable.
/// </summary>
internal delegate void AuditLogCreatedAtBinder(DbCommand command, string parameter, DateTimeOffset value);

/// <summary>
/// The audit log table as one provider sees it: its dialect, connection options, resolved names, JSON column type,
/// and how a <c>CreatedAt</c> value is bound. The schema contribution, writer, and reader share it, so the DDL and the
/// statements name the same objects.
/// </summary>
internal sealed class RelationalAuditLogTable
{
    private readonly AuditLogCreatedAtBinder? _bindCreatedAt;

    public RelationalAuditLogTable(
        ISqlDialect dialect,
        RelationalAuditLogOptions options,
        AuditLogStorageOptions storageOptions,
        AuditLogJsonColumnType defaultJsonColumnType,
        AuditLogCreatedAtBinder? bindCreatedAt = null
    )
    {
        Dialect = dialect;
        Options = options;
        Schema = storageOptions.Schema;
        // A configured name is used verbatim; the default follows the dialect's convention.
        Name = storageOptions.TableName ?? dialect.Name(AuditLogStorageNames.DefaultTableName);
        Qualified = dialect.Qualify(Schema, Name);
        JsonColumnType = (storageOptions.JsonColumnType ?? defaultJsonColumnType).ToSqlFragment();
        _bindCreatedAt = bindCreatedAt;
    }

    public ISqlDialect Dialect { get; }

    public RelationalAuditLogOptions Options { get; }

    public string Schema { get; }

    /// <summary>The unqualified, unquoted table name.</summary>
    public string Name { get; }

    /// <summary>The quoted, schema-qualified table.</summary>
    public string Qualified { get; }

    /// <summary>The SQL type of the JSON columns, which written values are cast to.</summary>
    public string JsonColumnType { get; }

    /// <summary>Returns a column, in the dialect's convention and quoted.</summary>
    public string Column(string pascalName) => Dialect.Quote(Dialect.Name(pascalName));

    public void BindCreatedAt(DbCommand command, string parameter, DateTimeOffset value)
    {
        if (_bindCreatedAt is null)
        {
            Dialect.AddParameter(command, parameter, SqlColumnType.Timestamp, value);
        }
        else
        {
            _bindCreatedAt(command, parameter, value);
        }
    }

    /// <summary>Reads a <c>CreatedAt</c> value as a UTC instant, whichever date and time type the column has.</summary>
    /// <exception cref="InvalidCastException">The column holds a value that is not a date and time.</exception>
    public static DateTimeOffset ReadCreatedAt(DbDataReader reader, int ordinal)
    {
        return reader.GetValue(ordinal) switch
        {
            DateTimeOffset instant => instant.ToUniversalTime(),
            // Drivers return a column without an offset as a DateTime; every writer stores UTC there.
            DateTime utc => new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)),
            var other => throw new InvalidCastException(
                $"The audit log CreatedAt column returned a {other.GetType().Name}; it must be a date and time type."
            ),
        };
    }
}
