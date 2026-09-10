// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Headless.MultiTenancy;

/// <summary>
/// EF Core configuration for <see cref="TenantRecord"/>: table name, column lengths, and a unique index
/// on <see cref="TenantRecord.NormalizedIdentifier"/> pinned to a deterministic, case- and
/// accent-sensitive collation (KTD6) so a lookup never matches a row differing only by case — the default
/// collation on SQL Server is case-insensitive, which would silently break the catalog service's ordinal
/// lookup contract (R7).
/// </summary>
/// <param name="providerName">
/// The active EF Core provider's invariant name (<see cref="Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade.ProviderName"/>),
/// used to select the provider-specific collation string. A provider with no known mapping (including
/// <see langword="null"/>, for example an in-memory or SQLite test provider) gets no <c>UseCollation</c>
/// call — the index still enforces uniqueness, just under that provider's default collation.
/// </param>
internal sealed class TenantRecordConfiguration(string? providerName) : IEntityTypeConfiguration<TenantRecord>
{
    private const string _TableName = "Tenants";

    // SQL Server's default collation is case-insensitive (and typically accent-insensitive); a binary
    // collation is the deterministic, byte-ordinal choice that matches the catalog service's ordinal
    // comparison contract.
    private const string _SqlServerCollation = "Latin1_General_100_BIN2";

    // PostgreSQL's "C" collation is byte-ordinal (case- and accent-sensitive) by default already; pinning
    // it explicitly documents the requirement and survives a future database- or cluster-level default change.
    private const string _PostgreSqlCollation = "C";

    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<TenantRecord> b)
    {
        b.ToTable(_TableName);
        b.ConfigureHeadlessConvention();

        b.Property(x => x.Id).HasMaxLength(DomainConstants.IdMaxLength);
        b.Property(x => x.Identifier).HasMaxLength(TenantRecordConstants.IdentifierMaxLength).IsRequired();
        b.Property(x => x.Name).HasMaxLength(TenantRecordConstants.NameMaxLength);

        var normalizedIdentifier = b.Property(x => x.NormalizedIdentifier)
            .HasMaxLength(TenantRecordConstants.IdentifierMaxLength)
            .IsRequired();

        var collation = _ResolveCollation(providerName);

        if (collation is not null)
        {
            normalizedIdentifier.UseCollation(collation);
        }

        b.HasIndex(x => x.NormalizedIdentifier).IsUnique().HasDatabaseName($"IX_{_TableName}_NormalizedIdentifier");
    }

    /// <summary>Maps an EF Core provider invariant name to its deterministic, case-sensitive collation string.</summary>
    private static string? _ResolveCollation(string? providerName)
    {
        return providerName switch
        {
            "Microsoft.EntityFrameworkCore.SqlServer" => _SqlServerCollation,
            "Npgsql.EntityFrameworkCore.PostgreSQL" => _PostgreSqlCollation,
            _ => null,
        };
    }
}
