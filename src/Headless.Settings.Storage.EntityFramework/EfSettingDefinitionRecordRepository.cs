// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Settings.Entities;
using Headless.Settings.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Headless.Settings.Storage.EntityFramework;

public sealed class EfSettingDefinitionRecordRepository<TContext>(IDbContextFactory<TContext> dbFactory)
    : ISettingDefinitionRecordRepository
    where TContext : DbContext, ISettingsDbContext
{
    public async Task<List<SettingDefinitionRecord>> GetListAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var list = await db.SettingDefinitions.ToListAsync(cancellationToken);

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

        db.SettingDefinitions.AddRange(addedRecords);
        db.SettingDefinitions.UpdateRange(changedRecords);
        db.SettingDefinitions.RemoveRange(deletedRecords);

        await db.SaveChangesAsync(cancellationToken);
    }
}
