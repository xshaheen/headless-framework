// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Primitives;
using Microsoft.EntityFrameworkCore;

namespace Headless.MultiTenancy;

/// <summary>
/// EF Core implementation of <see cref="ITenantStore"/> and <see cref="ITenantDirectory"/> that stores
/// <see cref="TenantRecord"/> entities via a pooled <typeparamref name="TContext"/>. Read-only: this
/// package ships no framework write path (KTD6) — apps insert and update <see cref="TenantRecord"/>
/// directly against their own <typeparamref name="TContext"/>.
/// </summary>
/// <typeparam name="TContext">The <see cref="DbContext"/> type registered with the DI container.</typeparam>
/// <param name="dbFactory">Factory used to create <typeparamref name="TContext"/> instances per operation.</param>
internal sealed class EfTenantStore<TContext>(IDbContextFactory<TContext> dbFactory) : ITenantStore, ITenantDirectory
    where TContext : DbContext
{
    /// <inheritdoc/>
    public async Task<TenantInfo?> FindByIdentifierAsync(
        string normalizedIdentifier,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(normalizedIdentifier);

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var record = await db.Set<TenantRecord>()
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.NormalizedIdentifier == normalizedIdentifier, cancellationToken)
            .ConfigureAwait(false);

        return record is null ? null : _ToTenantInfo(record);
    }

    /// <inheritdoc/>
    public async Task<TenantInfo?> FindByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNull(id);

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var record = await db.Set<TenantRecord>()
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            .ConfigureAwait(false);

        return record is null ? null : _ToTenantInfo(record);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<TenantInfo>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var records = await db.Set<TenantRecord>().AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);

        return records.ConvertAll(_ToTenantInfo);
    }

    private static TenantInfo _ToTenantInfo(TenantRecord record)
    {
        // TenantInfo.Identifier carries the already-normalized form (its own doc contract): the catalog
        // service and every other shipped store follow the same rule, so lookups compare ordinally
        // without re-normalizing.
        return new TenantInfo(record.Id, record.NormalizedIdentifier, record.Name, record.IsEnabled)
        {
            ExtraProperties = new ExtraProperties(record.ExtraProperties),
        };
    }
}
