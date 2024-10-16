// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Settings.Entities;

namespace Framework.Settings.Values;

public interface ISettingValueRecordRepository
{
    Task<SettingRecord?> FindAsync(
        string name,
        string? providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    );

    Task<List<SettingRecord>> GetListAsync(
        string[] names,
        string? providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    );

    Task<List<SettingRecord>> GetListAsync(
        string? providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    );

    Task InsertAsync(SettingRecord setting);

    Task UpdateAsync(SettingRecord setting);

    Task DeleteAsync(SettingRecord setting);
}
