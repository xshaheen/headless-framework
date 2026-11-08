// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using Headless.Fencing;
using Headless.Sql;
using Headless.Sql.PostgreSql;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Headless.Jobs.Infrastructure;

/// <summary>
/// Renders the column assignments a lease transition writes alongside the lease, from the entity's EF Core model.
/// </summary>
/// <remarks>
/// Column names come from the model, so a renamed column or a non-default naming convention is honored, and each
/// value is bound through the property's own type mapping, so its value converter (enum-as-text, UTC instants, the
/// text form SQLite stores instants in) applies exactly as it does for an EF Core write.
/// </remarks>
internal sealed class JobsRowSet
{
    // Not "Lease…": FencedRows reserves that prefix, and "now"/"applied", for its own parameters.
    private const string _ParameterPrefix = "JobsSet";

    private readonly IEntityType _entity;
    private readonly StoreObjectIdentifier _store;
    private readonly ISqlGenerationHelper _sql;
    private readonly ISqlDialect _dialect;
    private readonly List<string> _assignments = [];
    private readonly List<(RelationalTypeMapping Mapping, string Name, object? Value)> _parameters = [];

    private JobsRowSet(DbContext context, Type entityType)
    {
        _entity =
            context.Model.FindEntityType(entityType)
            ?? throw new InvalidOperationException($"{entityType.Name} is not mapped by the Jobs DbContext.");
        var tableName =
            _entity.GetTableName()
            ?? throw new InvalidOperationException($"{entityType.Name} is not mapped to a relational table.");
        _store = StoreObjectIdentifier.Table(tableName, _entity.GetSchema());
        _sql = context.GetService<ISqlGenerationHelper>();
        _dialect = JobsSqlDialects.Resolve(context);
    }

    /// <summary>Starts an empty set of assignments for rows of <typeparamref name="TEntity"/>.</summary>
    public static JobsRowSet For<TEntity>(DbContext context)
        where TEntity : class
    {
        return new JobsRowSet(context, typeof(TEntity));
    }

    /// <summary>Whether no assignment was added.</summary>
    public bool IsEmpty => _assignments.Count == 0;

    /// <summary>The delimited column of <paramref name="propertyName"/>.</summary>
    public string Column(string propertyName) => _sql.DelimitIdentifier(_Property(propertyName).Column);

    /// <summary>Binds <paramref name="value"/> as a parameter typed like <paramref name="propertyName"/>, and returns its placeholder.</summary>
    public string Parameter(string propertyName, object? value)
    {
        var name = _ParameterPrefix + _parameters.Count.ToString(CultureInfo.InvariantCulture);
        _parameters.Add((_Property(propertyName).Mapping, name, value));

        return "@" + name;
    }

    /// <summary>Binds <paramref name="value"/> as a parameter typed like the lease's <paramref name="propertyName"/>, and returns its placeholder.</summary>
    public string LeaseParameter(string propertyName, object? value)
    {
        var name = _ParameterPrefix + _parameters.Count.ToString(CultureInfo.InvariantCulture);
        _parameters.Add((_LeaseProperty(propertyName), name, value));

        return "@" + name;
    }

    /// <summary>Assigns <paramref name="value"/> to <paramref name="propertyName"/>.</summary>
    public JobsRowSet Value(string propertyName, object? value)
    {
        return Expression(propertyName, Parameter(propertyName, value));
    }

    /// <summary>Assigns the database clock's current instant to a <see cref="DateTime"/> property.</summary>
    /// <remarks>
    /// The clock is read per statement, so a write that runs inside a transaction still stamps its own instant, and
    /// the instant is taken on the database, so a skewed node clock cannot move it.
    /// </remarks>
    public JobsRowSet DatabaseNow(string propertyName)
    {
        return Expression(propertyName, DatabaseNowSql(propertyName));
    }

    /// <summary>Assigns the database clock's current instant plus <paramref name="offset"/> to a <see cref="DateTime"/> property.</summary>
    /// <remarks>The due time is taken on the database, so a skewed node clock cannot move it.</remarks>
    public JobsRowSet DatabaseNowPlus(string propertyName, TimeSpan offset)
    {
        return Expression(propertyName, DatabaseNowSql(propertyName, offset));
    }

    /// <summary>The database clock's current instant, plus <paramref name="offset"/>, in the store type of <paramref name="propertyName"/>.</summary>
    public string DatabaseNowSql(string propertyName, TimeSpan offset = default)
    {
        var columnType = _Property(propertyName).ColumnType;
        // Whole milliseconds, rendered invariant: the offset is a computed number, never caller text.
        var milliseconds = (long)Math.Round(offset.TotalMilliseconds, MidpointRounding.ToEven);
        var ms = milliseconds.ToString(CultureInfo.InvariantCulture);

        if (JobsSqlDialects.IsSqlite(_dialect))
        {
            // EF Core's SQLite provider stores a DateTime as this trimmed text, so the stamp sorts and compares with
            // the values EF Core writes.
            var modifier =
                milliseconds == 0
                    ? string.Empty
                    : ", '"
                        + (milliseconds / 1000m).ToString("+0.###;-0.###", CultureInfo.InvariantCulture)
                        + " seconds'";

            return $"rtrim(rtrim(strftime('%Y-%m-%d %H:%M:%f', 'now'{modifier}), '0'), '.')";
        }

        if (_dialect is PostgreSqlDialect)
        {
            var now = columnType.StartsWith("timestamp without time zone", StringComparison.OrdinalIgnoreCase)
                ? "(clock_timestamp() AT TIME ZONE 'UTC')"
                : "clock_timestamp()";

            return milliseconds == 0 ? now : $"({now} + {ms} * interval '1 millisecond')";
        }

        // DATEADD takes an int, so a long offset is split into whole seconds and the remaining milliseconds.
        return milliseconds == 0
            ? "SYSUTCDATETIME()"
            : "DATEADD(millisecond, "
                + (milliseconds % 1000).ToString(CultureInfo.InvariantCulture)
                + ", DATEADD(second, "
                + (milliseconds / 1000).ToString(CultureInfo.InvariantCulture)
                + ", SYSUTCDATETIME()))";
    }

    /// <summary>Assigns trusted SQL <paramref name="sql"/> to <paramref name="propertyName"/>.</summary>
    public JobsRowSet Expression(string propertyName, string sql)
    {
        _assignments.Add($"{Column(propertyName)} = {sql}");

        return this;
    }

    /// <summary>The assignments as the write a transition composes into its statement.</summary>
    /// <param name="condition">An extra predicate over the row, for <c>WriteAsync</c> only.</param>
    public FencedRowWrite ToWrite(string? condition = null)
    {
        var parameters = _parameters.ToArray();

        return new FencedRowWrite(
            Set: _assignments.Count == 0 ? null : string.Join(", ", _assignments),
            Condition: condition,
            Bind: parameters.Length == 0
                ? null
                : command =>
                {
                    foreach (var (mapping, name, value) in parameters)
                    {
                        command.Parameters.Add(mapping.CreateParameter(command, name, value));
                    }
                }
        );
    }

    private RelationalTypeMapping _LeaseProperty(string propertyName)
    {
        var property =
            _entity.FindComplexProperty(nameof(TimeJobEntity.Lease))?.ComplexType.FindProperty(propertyName)
            ?? throw new InvalidOperationException($"{_entity.ClrType.Name}.Lease.{propertyName} is not mapped.");

        return property.GetRelationalTypeMapping()
            ?? throw new InvalidOperationException($"{_entity.ClrType.Name}.Lease.{propertyName} has no type mapping.");
    }

    private (string Column, RelationalTypeMapping Mapping, string ColumnType) _Property(string propertyName)
    {
        var property =
            _entity.FindProperty(propertyName)
            ?? throw new InvalidOperationException($"{_entity.ClrType.Name}.{propertyName} is not mapped.");
        var column =
            property.GetColumnName(_store)
            ?? throw new InvalidOperationException($"{_entity.ClrType.Name}.{propertyName} has no column mapping.");
        var mapping =
            property.GetRelationalTypeMapping()
            ?? throw new InvalidOperationException($"{_entity.ClrType.Name}.{propertyName} has no type mapping.");

        return (column, mapping, property.GetColumnType(_store) ?? mapping.StoreType);
    }
}
