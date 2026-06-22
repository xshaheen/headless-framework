// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Settings.Entities;
using Headless.Settings.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Headless.Settings;

/// <summary>
/// EF Core implementation of <see cref="ISettingDefinitionRecordRepository"/> that stores
/// <see cref="SettingDefinitionRecord"/> entities via a pooled <typeparamref name="TContext"/>.
/// </summary>
/// <typeparam name="TContext">The <see cref="DbContext"/> type registered with the DI container.</typeparam>
/// <param name="dbFactory">Factory used to create <typeparamref name="TContext"/> instances per operation.</param>
public sealed class EfSettingDefinitionRecordRepository<TContext>(IDbContextFactory<TContext> dbFactory)
    : ISettingDefinitionRecordRepository
    where TContext : DbContext
{
    /// <inheritdoc/>
    public async Task<List<SettingDefinitionRecord>> GetListAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var list = await db.Set<SettingDefinitionRecord>()
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return list;
    }

    /// <inheritdoc/>
    public async Task SaveAsync(
        List<SettingDefinitionRecord> addedRecords,
        List<SettingDefinitionRecord> changedRecords,
        List<SettingDefinitionRecord> deletedRecords,
        CancellationToken cancellationToken = default
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        db.Set<SettingDefinitionRecord>().AddRange(addedRecords);
        db.Set<SettingDefinitionRecord>().UpdateRange(changedRecords);
        db.Set<SettingDefinitionRecord>().RemoveRange(deletedRecords);

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
