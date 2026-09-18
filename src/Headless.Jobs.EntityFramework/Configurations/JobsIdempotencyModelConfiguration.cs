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

    internal static void Configure(ModelBuilder builder, string schema, string? contractCollation)
    {
        builder.Entity<JobIdempotencyReservationEntity>(entity =>
        {
            entity.ToTable(TableName, schema);
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
        var requiredCollation = JobsContractCollation.Require(
            context.Database.ProviderName,
            "Idempotent enqueue requires PostgreSQL, SQL Server, or the in-memory provider."
        );

        var model = context.GetService<IDesignTimeModel>().Model;
        var entity = model.FindEntityType(typeof(JobIdempotencyReservationEntity));
        if (entity is null || entity.GetTableName() is null)
        {
            // Unlike the keyed twin (whose entity is the consumer's own TTimeJob, always mapped), the reservation
            // entity is mapped only by the built-in customizer or FinalizeJobsModel — fail with the fix, not an NRE.
            throw new InvalidOperationException(
                "Idempotent enqueue requires the reservation mapping. Call modelBuilder.FinalizeJobsModel<TTimeJob>(this) "
                    + "at the end of OnModelCreating after all consumer mappings, then initialize the database from that model."
            );
        }

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
