// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Settings.Definitions;
using Framework.Settings.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Framework.Settings.Storage.EntityFramework;

public sealed class EfSettingDefinitionRecordRepository(IServiceScopeFactory scopeFactory)
    : ISettingDefinitionRecordRepository
{
    public async Task<List<SettingDefinitionRecord>> GetListAsync(CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SettingsDbContext>();

        return await db.SettingDefinitions.ToListAsync(cancellationToken);
    }

    public async Task SaveAsync(
        List<SettingDefinitionRecord> addedRecords,
        List<SettingDefinitionRecord> changedRecords,
        List<SettingDefinitionRecord> deletedRecords,
        CancellationToken cancellationToken = default
    )
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SettingsDbContext>();

        db.SettingDefinitions.AddRange(addedRecords);
        db.SettingDefinitions.UpdateRange(changedRecords);
        db.SettingDefinitions.RemoveRange(deletedRecords);

        await db.SaveChangesAsync(cancellationToken);
    }
}
