// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Settings.Definitions;
using Framework.Settings.Entities;
using Microsoft.EntityFrameworkCore;

namespace Framework.Settings.Storage.EntityFramework;

public sealed class EfSettingDefinitionRecordRepository(IDbContextFactory<SettingsDbContext> dbFactory)
    : ISettingDefinitionRecordRepository
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
