// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Linq.Expressions;
using System.Reflection;
using Headless.EntityFramework;
using Headless.Hosting;
using Headless.Jobs.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;

namespace Headless.Jobs;

/// <summary>
/// Maps the change marker the database stamps on every Jobs row: the column, its insert default, its
/// <c>(ChangedAt, Id)</c> index, and the translation of <see cref="JobsChangeMarker.Next"/>, the setter every update
/// chains.
/// </summary>
/// <remarks>
/// Everything lives in the entity configurations, which every Jobs model applies, so a consumer that maps the Jobs
/// tables by hand gets the marker too. The configurations do not receive the database provider, so the insert default
/// reads it from the annotations the provider's conventions put on the model before any configuration runs, and the
/// update translation reads it from the column's type mapping.
/// </remarks>
internal static class JobsChangeMarkerModelConfiguration
{
    private const string _NpgsqlAssembly = "Npgsql.EntityFrameworkCore.PostgreSQL";
    private const string _SqlServerAssembly = "Microsoft.EntityFrameworkCore.SqlServer";

    private static readonly MethodInfo _NextMethod = typeof(JobsChangeMarker).GetMethod(
        nameof(JobsChangeMarker.Next),
        BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly,
        [typeof(DateTime)]
    )!;

    private static readonly BoolTypeMapping _SqlServerBoolMapping = new("bit");

    private enum Store
    {
        Other = 0,
        PostgreSql = 1,
        SqlServer = 2,
    }

    /// <summary>
    /// Maps the change-marker column of one Jobs table, its insert default, and its <c>(ChangedAt, Id)</c> index, the
    /// range a change read seeks; and registers the database function every update stamps the column through.
    /// </summary>
    internal static void ConfigureColumn(EntityTypeBuilder builder, StorageNamingStyle style, string table)
    {
        var model = builder.Metadata.Model;
        var store = _StoreOf(model);

        var property = builder
            .Property<DateTime>(nameof(BaseJobEntity.ChangedAt))
            .HasConversion(new NormalizeDateTimeValueConverter())
            // A raw-SQL insert that never names the column takes the default too.
            .HasDefaultValueSql(_InsertClockSql(store));

        // The database owns the value: an insert takes the column default and an update stamps it through
        // JobsChangeMarker.Next, so a caller's value never reaches the column.
        property.Metadata.SetBeforeSaveBehavior(PropertySaveBehavior.Ignore);
        property.Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);

        builder
            .HasIndex(nameof(BaseJobEntity.ChangedAt), nameof(BaseJobEntity.Id))
            .HasDatabaseName(
                HeadlessStorageNaming.IndexName(style, table, nameof(BaseJobEntity.ChangedAt), nameof(BaseJobEntity.Id))
            );

        if (model.FindDbFunction(_NextMethod) is null)
        {
            model.AddDbFunction(_NextMethod).Translation = arguments => _TranslateNext(arguments[0]);
        }
    }

    private static Store _StoreOf(IReadOnlyAnnotatable model)
    {
        foreach (var annotation in model.GetAnnotations())
        {
            if (annotation.Name.StartsWith("Npgsql:", StringComparison.Ordinal))
            {
                return Store.PostgreSql;
            }

            if (annotation.Name.StartsWith("SqlServer:", StringComparison.Ordinal))
            {
                return Store.SqlServer;
            }
        }

        return Store.Other;
    }

    private static Store _StoreOf(RelationalTypeMapping? mapping)
    {
        return mapping?.GetType().Assembly.GetName().Name switch
        {
            _NpgsqlAssembly => Store.PostgreSql,
            _SqlServerAssembly => Store.SqlServer,
            _ => Store.Other,
        };
    }

    private static string _InsertClockSql(Store store)
    {
        return store switch
        {
            Store.PostgreSql => "clock_timestamp()",
            Store.SqlServer => "SYSUTCDATETIME()",
            // Another database stamps from its own current-time function, which may be a transaction clock and gives
            // no per-row ordering guarantee beyond it.
            _ => "CURRENT_TIMESTAMP",
        };
    }

    private static SqlExpression _TranslateNext(SqlExpression previous)
    {
        var mapping = previous.TypeMapping;

        switch (_StoreOf(mapping))
        {
            case Store.PostgreSql:
            {
                // clock_timestamp() is the statement clock; now() would be the transaction-start clock.
                var clock = new SqlFragmentExpression("clock_timestamp()", typeof(DateTime), mapping);
                // PostgreSQL timestamps count microseconds, its smallest step.
                var nextTick = new SqlBinaryExpression(
                    ExpressionType.Add,
                    previous,
                    new SqlFragmentExpression("INTERVAL '1 microsecond'", typeof(TimeSpan), typeMapping: null),
                    typeof(DateTime),
                    mapping
                );

                return new SqlFunctionExpression(
                    "GREATEST",
                    [clock, nextTick],
                    nullable: true,
                    argumentsPropagateNullability: [false, false],
                    typeof(DateTime),
                    mapping
                );
            }
            case Store.SqlServer:
            {
                // datetime2 counts 100-nanosecond ticks. A CASE rather than GREATEST, which needs SQL Server 2022.
                var clock = new SqlFragmentExpression("SYSUTCDATETIME()", typeof(DateTime), mapping);
                var nextTick = new SqlFunctionExpression(
                    "DATEADD",
                    [
                        new SqlFragmentExpression("nanosecond", typeof(string), typeMapping: null),
                        new SqlFragmentExpression("100", typeof(int), typeMapping: null),
                        previous,
                    ],
                    nullable: true,
                    argumentsPropagateNullability: [false, false, true],
                    typeof(DateTime),
                    mapping
                );

                return new CaseExpression(
                    [
                        new CaseWhenClause(
                            new SqlBinaryExpression(
                                ExpressionType.GreaterThan,
                                nextTick,
                                clock,
                                typeof(bool),
                                // The comparison is a CASE condition, which SQL Server's generator requires to carry a
                                // type mapping; the translation runs without the provider's mapping source.
                                _SqlServerBoolMapping
                            ),
                            nextTick
                        ),
                    ],
                    clock
                );
            }
            default:
                return new SqlFragmentExpression("CURRENT_TIMESTAMP", typeof(DateTime), mapping);
        }
    }
}
