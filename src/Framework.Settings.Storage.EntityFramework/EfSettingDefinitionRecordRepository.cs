// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Settings.Definitions;
using Framework.Settings.Entities;
using Microsoft.EntityFrameworkCore;

namespace Framework.Settings.Storage.EntityFramework;

public sealed class EfSettingDefinitionRecordRepository(SettingsDbContext db) : ISettingDefinitionRecordRepository
{
    public Task<List<SettingDefinitionRecord>> GetListAsync(CancellationToken cancellationToken = default)
    {
        return db.SettingDefinitions.ToListAsync(cancellationToken);
    }

    public async Task SaveAsync(
        List<SettingDefinitionRecord> addedRecords,
        List<SettingDefinitionRecord> changedRecords,
        List<SettingDefinitionRecord> deletedRecords,
        CancellationToken cancellationToken = default
    )
    {
        db.SettingDefinitions.AddRange(addedRecords);
        db.SettingDefinitions.UpdateRange(changedRecords);
        db.SettingDefinitions.RemoveRange(deletedRecords);

        await db.SaveChangesAsync(cancellationToken);
    }
}
