// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Entities;
using Headless.Jobs.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage;

namespace Headless.Jobs.Configurations;

// Model mapping for the Jobs-owned idempotency reservation table. Uniqueness is the composite primary key on
// (ScopeKey, Function, ContractVersion, IdempotencyKey) — never a nullable-tenant unique index, whose semantics
// differ between PostgreSQL and SQL Server. ExpiresAt carries a non-unique index so a future sweeper can find
// expired rows; correctness never depends on sweeping.
internal static class JobsIdempotencyModelConfiguration
{
    internal const string TableName = "TimeJobIdempotencyReservations";
    internal const string Schema = JobDbConstants.DefaultSchema;

    internal static void Configure(ModelBuilder builder, string? contractCollation)
    {
        builder.Entity<JobIdempotencyReservationEntity>(entity =>
        {
            entity.ToTable(TableName, Schema);
            entity.HasKey(row => new
            {
                row.ScopeKey,
                row.Function,
                row.ContractVersion,
                row.IdempotencyKey,
            });
            entity.Property(row => row.ScopeKey).IsRequired().HasMaxLength(2 + JobsTenancyOptions.TenantIdMaxLength);
            entity
                .Property(row => row.Function)
                .IsRequired()
                .HasMaxLength(JobContract.NameMaxLength)
                .HasConversion(value => JobContract.ValidateName(value), value => value);
            entity
                .Property(row => row.ContractVersion)
                .IsRequired()
                .HasMaxLength(JobContract.VersionMaxLength)
                .HasConversion(value => JobContract.ValidateVersion(value), value => value);
            entity.Property(row => row.IdempotencyKey).IsRequired().HasMaxLength(JobContract.NameMaxLength);
            entity.Property(row => row.TenantId).IsRequired(false).HasMaxLength(JobsTenancyOptions.TenantIdMaxLength);
            entity.HasIndex(row => row.ExpiresAt).HasDatabaseName("IX_TimeJobIdempotencyReservations_ExpiresAt");
        });

        if (contractCollation is not null)
        {
            var entity = builder.Entity<JobIdempotencyReservationEntity>();
            entity.Property(row => row.ScopeKey).UseCollation(contractCollation);
            entity.Property(row => row.Function).UseCollation(contractCollation);
            entity.Property(row => row.ContractVersion).UseCollation(contractCollation);
            entity.Property(row => row.IdempotencyKey).UseCollation(contractCollation);
        }
    }

    // Same ordinal-collation discipline as keyed scheduling: identity columns must compare binary/ordinal, or a
    // case-insensitive database collation could merge two caller-distinct keys into one reservation.
    internal static void ValidateOrdinalScope(DbContext context)
    {
        var requiredCollation = context.Database.ProviderName switch
        {
            "Npgsql.EntityFrameworkCore.PostgreSQL" => "C",
            "Microsoft.EntityFrameworkCore.SqlServer" => "Latin1_General_100_BIN2",
            _ => throw new NotSupportedException(
                "Idempotent enqueue requires PostgreSQL, SQL Server, or the in-memory provider."
            ),
        };

        var model = context.GetService<IDesignTimeModel>().Model;
        var entity = model.FindEntityType(typeof(JobIdempotencyReservationEntity))!;
        var table = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
        foreach (
            var name in new[]
            {
                nameof(JobIdempotencyReservationEntity.ScopeKey),
                nameof(JobIdempotencyReservationEntity.Function),
                nameof(JobIdempotencyReservationEntity.ContractVersion),
                nameof(JobIdempotencyReservationEntity.IdempotencyKey),
            }
        )
        {
            var collation = entity.FindProperty(name)!.GetCollation(table) ?? model.GetCollation();
            if (!string.Equals(collation, requiredCollation, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Idempotent enqueue requires explicit collation '{requiredCollation}' for {nameof(JobIdempotencyReservationEntity)}.{name}. "
                        + "Pass contractCollation to the reservation configuration (or the matching model default) and initialize the database from that model."
                );
            }
        }
    }
}
