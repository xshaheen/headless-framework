// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Kernel.BuildingBlocks.Abstractions;
using Framework.Settings.Entities;
using Framework.Settings.Values;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Framework.Settings.Storage.EntityFramework;

public sealed class EfSettingValueRecordRepository(IServiceScopeFactory scopeFactory) : ISettingValueRecordRepository
{
    public async Task<SettingValueRecord?> FindAsync(
        string name,
        string? providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    )
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SettingsDbContext>();

        return await db
            .SettingValues.OrderBy(x => x.Id)
            .FirstOrDefaultAsync(
                s => s.Name == name && s.ProviderName == providerName && s.ProviderKey == providerKey,
                cancellationToken
            );
    }

    public async Task<List<SettingValueRecord>> GetListAsync(
        string[] names,
        string? providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    )
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SettingsDbContext>();

        return await db
            .SettingValues.Where(s =>
                names.Contains(s.Name) && s.ProviderName == providerName && s.ProviderKey == providerKey
            )
            .ToListAsync(cancellationToken);
    }

    public async Task<List<SettingValueRecord>> GetListAsync(
        string? providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    )
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SettingsDbContext>();

        return await db
            .SettingValues.Where(s => s.ProviderName == providerName && s.ProviderKey == providerKey)
            .ToListAsync(cancellationToken);
    }

    public async Task InsertAsync(SettingValueRecord setting, CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SettingsDbContext>();
        db.SettingValues.Add(setting);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(SettingValueRecord setting, CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SettingsDbContext>();
        db.SettingValues.Update(setting);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(SettingValueRecord setting, CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SettingsDbContext>();
        db.SettingValues.Remove(setting);
        await db.SaveChangesAsync(cancellationToken);
    }
}
