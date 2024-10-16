// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Kernel.BuildingBlocks.Abstractions;
using Framework.Settings.Entities;
using Framework.Settings.Values;
using Microsoft.EntityFrameworkCore;

namespace Framework.Settings.Storage.EntityFramework;

public sealed class EfSettingValueRecordRepository(
    SettingsDbContext db,
    ICancellationTokenProvider cancellationTokenProvider
) : ISettingValueRecordRepository
{
    public Task<SettingValueRecord?> FindAsync(
        string name,
        string? providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    )
    {
        return db
            .SettingValues.OrderBy(x => x.Id)
            .FirstOrDefaultAsync(
                s => s.Name == name && s.ProviderName == providerName && s.ProviderKey == providerKey,
                cancellationTokenProvider.FallbackToProvider(cancellationToken)
            );
    }

    public Task<List<SettingValueRecord>> GetListAsync(
        string[] names,
        string? providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    )
    {
        return db
            .SettingValues.Where(s =>
                names.Contains(s.Name) && s.ProviderName == providerName && s.ProviderKey == providerKey
            )
            .ToListAsync(cancellationTokenProvider.FallbackToProvider(cancellationToken));
    }

    public Task<List<SettingValueRecord>> GetListAsync(
        string? providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    )
    {
        return db
            .SettingValues.Where(s => s.ProviderName == providerName && s.ProviderKey == providerKey)
            .ToListAsync(cancellationTokenProvider.FallbackToProvider(cancellationToken));
    }

    public Task InsertAsync(SettingValueRecord setting)
    {
        db.SettingValues.Add(setting);
        return db.SaveChangesAsync(cancellationTokenProvider.Token);
    }

    public Task UpdateAsync(SettingValueRecord setting)
    {
        db.SettingValues.Update(setting);
        return db.SaveChangesAsync(cancellationTokenProvider.Token);
    }

    public Task DeleteAsync(SettingValueRecord setting)
    {
        db.SettingValues.Remove(setting);
        return db.SaveChangesAsync(cancellationTokenProvider.Token);
    }
}
