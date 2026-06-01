// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Settings.Entities;
using Headless.Settings.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Headless.Settings;

public sealed class EfSettingDefinitionRecordRepository<TContext>(IDbContextFactory<TContext> dbFactory)
    : ISettingDefinitionRecordRepository
    where TContext : DbContext
{
    public async Task<List<SettingDefinitionRecord>> GetListAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var list = await db.Set<SettingDefinitionRecord>().AsNoTracking().ToListAsync(cancellationToken);

        return list;
    }

    public async Task SaveAsync(
        List<SettingDefinitionRecord> addedRecords,
        List<SettingDefinitionRecord> changedRecords,
        List<SettingDefinitionRecord> deletedRecords,
        CancellationToken cancellationToken = default
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        db.Set<SettingDefinitionRecord>().AddRange(addedRecords);
        db.Set<SettingDefinitionRecord>().UpdateRange(changedRecords);
        db.Set<SettingDefinitionRecord>().RemoveRange(deletedRecords);

        await db.SaveChangesAsync(cancellationToken);
    }
}
