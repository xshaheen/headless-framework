// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage;

namespace Headless.Jobs;

internal static class JobsKeyedModelConfiguration
{
    // Named after the default time-job table rather than its final mapping: a consumer mapping may rename the table
    // after these are built, and the keyed-operation validator must still find exactly the names written here.
    private static string _Table(StorageNamingStyle style) =>
        JobsStorageNaming.Table(style, JobsStorageNaming.TimeJobs);

    private static string _KeyGenerationTenantIndex(StorageNamingStyle style) =>
        JobsStorageNaming.Name(style, "UX", _Table(style), "KeyGeneration", "Tenant");

    private static string _KeyGenerationSystemIndex(StorageNamingStyle style) =>
        JobsStorageNaming.Name(style, "UX", _Table(style), "KeyGeneration", "System");

    private static string _CurrentKeyTenantIndex(StorageNamingStyle style) =>
        JobsStorageNaming.Name(style, "UX", _Table(style), "CurrentKey", "Tenant");

    private static string _CurrentKeySystemIndex(StorageNamingStyle style) =>
        JobsStorageNaming.Name(style, "UX", _Table(style), "CurrentKey", "System");

    private static string _KeyedMetadataCheck(StorageNamingStyle style) =>
        JobsStorageNaming.Name(style, "CK", _Table(style), "KeyedMetadata");

    internal static void ValidateOrdinalScope<TTimeJob>(DbContext context)
        where TTimeJob : TimeJobEntity<TTimeJob>, new()
    {
        var requiredCollation = JobsContractCollation.Require(
            context.Database.ProviderName,
            "Keyed Jobs require PostgreSQL, SQL Server, or the in-memory provider."
        );

        // Collations are omitted from EF's runtime model; inspect the finalized model used to generate the schema.
        var model = context.GetService<IDesignTimeModel>().Model;
        var entity = model.FindEntityType(typeof(TTimeJob))!;
        var style = JobsStorageNaming.StyleOf(context);
        var tableName = entity.GetTableName()!;
        string[] requiredUniqueIndexes =
        [
            _KeyGenerationTenantIndex(style),
            _KeyGenerationSystemIndex(style),
            _CurrentKeyTenantIndex(style),
            _CurrentKeySystemIndex(style),
        ];
        if (
            entity.FindCheckConstraint(_KeyedMetadataCheck(style)) is null
            || requiredUniqueIndexes.Any(name =>
                entity.FindIndex(name) is not { IsUnique: true } index || index.GetFilter() is null
            )
        )
        {
            throw new InvalidOperationException(
                "Keyed Jobs require finalized indexes and check constraints. Call modelBuilder.FinalizeJobsModel<TTimeJob>(this) "
                    + "at the end of OnModelCreating after all consumer mappings, then initialize the database from that model."
            );
        }

        var table = StoreObjectIdentifier.Table(tableName, entity.GetSchema());
        foreach (
            var name in new[]
            {
                nameof(TimeJobEntity.Function),
                nameof(TimeJobEntity.TenantId),
                nameof(TimeJobEntity.BusinessKey),
            }
        )
        {
            var collation = entity.FindProperty(name)!.GetCollation(table) ?? model.GetCollation();
            if (!string.Equals(collation, requiredCollation, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Keyed Jobs require explicit collation '{requiredCollation}' for {typeof(TTimeJob).Name}.{name}. "
                        + "Configure TimeJobConfigurations with contractCollation (or the matching model default) and apply the consumer migration before using keyed operations."
                );
            }
        }
    }

    internal static void Configure<TTimeJob>(ModelBuilder builder, DbContext context)
        where TTimeJob : TimeJobEntity<TTimeJob>, new()
    {
        var sql = context.GetService<ISqlGenerationHelper>();
        var trueLiteral = context
            .GetService<IRelationalTypeMappingSource>()
            .FindMapping(typeof(bool))!
            .GenerateSqlLiteral(value: true);
        Configure(builder.Entity<TTimeJob>(), JobsStorageNaming.StyleOf(context), sql.DelimitIdentifier, trueLiteral);
    }

    internal static void Configure<TTimeJob>(
        EntityTypeBuilder<TTimeJob> builder,
        StorageNamingStyle style,
        Func<string, string> quote,
        string trueLiteral
    )
        where TTimeJob : TimeJobEntity<TTimeJob>, new()
    {
        var tableName = builder.Metadata.GetTableName()!;
        var table = StoreObjectIdentifier.Table(tableName, builder.Metadata.GetSchema());
        string column(string property) => quote(builder.Metadata.FindProperty(property)!.GetColumnName(table)!);
        var key = column(nameof(TimeJobEntity.BusinessKey));
        var tenant = column(nameof(TimeJobEntity.TenantId));
        var fingerprint = column(nameof(TimeJobEntity.IntentFingerprint));
        var algorithm = column(nameof(TimeJobEntity.FingerprintAlgorithm));
        var generation = column(nameof(TimeJobEntity.Generation));
        var current = column(nameof(TimeJobEntity.IsCurrentGeneration));
        var parent = column(nameof(TimeJobEntity.ParentId));
        var condition = column(nameof(TimeJobEntity.RunCondition));
        var tenantFilter = $"{key} IS NOT NULL AND {tenant} IS NOT NULL";
        var systemFilter = $"{key} IS NOT NULL AND {tenant} IS NULL";
        builder
            .HasIndex(
                row => new
                {
                    row.TenantId,
                    row.Function,
                    row.BusinessKey,
                    row.Generation,
                },
                _KeyGenerationTenantIndex(style)
            )
            .IsUnique()
            .HasFilter(tenantFilter);
        builder
            .HasIndex(
                row => new
                {
                    row.Function,
                    row.BusinessKey,
                    row.Generation,
                },
                _KeyGenerationSystemIndex(style)
            )
            .IsUnique()
            .HasFilter(systemFilter);
        builder
            .HasIndex(
                row => new
                {
                    row.TenantId,
                    row.Function,
                    row.BusinessKey,
                },
                _CurrentKeyTenantIndex(style)
            )
            .IsUnique()
            .HasFilter($"{tenantFilter} AND {current} = {trueLiteral}");
        builder
            .HasIndex(row => new { row.Function, row.BusinessKey }, _CurrentKeySystemIndex(style))
            .IsUnique()
            .HasFilter($"{systemFilter} AND {current} = {trueLiteral}");
        builder.ToTable(mapping =>
            mapping.HasCheckConstraint(
                _KeyedMetadataCheck(style),
                $"({key} IS NULL AND {fingerprint} IS NULL AND {algorithm} IS NULL AND {generation} IS NULL AND {current} IS NULL) OR "
                    + $"({key} IS NOT NULL AND {key} <> '' AND {fingerprint} IS NOT NULL AND {fingerprint} <> '' AND {algorithm} IS NOT NULL AND {algorithm} <> '' AND {generation} IS NOT NULL AND {generation} > 0 AND {current} IS NOT NULL AND {parent} IS NULL AND {condition} IS NULL)"
            )
        );
    }
}
