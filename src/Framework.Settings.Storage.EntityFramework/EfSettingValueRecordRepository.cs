// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Settings.Entities;
using Framework.Settings.Values;
using Microsoft.EntityFrameworkCore;

namespace Framework.Settings.Storage.EntityFramework;

public sealed class EfSettingValueRecordRepository(IDbContextFactory<SettingsDbContext> dbFactory)
    : ISettingValueRecordRepository
{
    public async Task<SettingValueRecord?> FindAsync(
        string name,
        string providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        return await db
            .SettingValues.OrderBy(x => x.Id)
            .FirstOrDefaultAsync(
                s => s.Name == name && s.ProviderName == providerName && s.ProviderKey == providerKey,
                cancellationToken
            );
    }

    public async Task<List<SettingValueRecord>> FindAllAsync(
        string name,
        string? providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var query = db.SettingValues.Where(s => s.Name == name);

        if (providerName != null)
        {
            query = query.Where(s => s.ProviderName == providerName);
        }

        if (providerKey != null)
        {
            query = query.Where(s => s.ProviderKey == providerKey);
        }

        return await query.ToListAsync(cancellationToken);
    }

    public async Task<List<SettingValueRecord>> GetListAsync(
        string[] names,
        string providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        return await db
            .SettingValues.Where(s =>
                names.Contains(s.Name) && s.ProviderName == providerName && s.ProviderKey == providerKey
            )
            .ToListAsync(cancellationToken);
    }

    public async Task<List<SettingValueRecord>> GetListAsync(
        string providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        return await db
            .SettingValues.Where(s => s.ProviderName == providerName && s.ProviderKey == providerKey)
            .ToListAsync(cancellationToken);
    }

    public async Task InsertAsync(SettingValueRecord setting, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        db.SettingValues.Add(setting);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(SettingValueRecord setting, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        db.SettingValues.Update(setting);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(
        IEnumerable<SettingValueRecord> settings,
        CancellationToken cancellationToken = default
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        db.SettingValues.RemoveRange(settings);
        await db.SaveChangesAsync(cancellationToken);
    }
}
