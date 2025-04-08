// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Settings.Entities;

namespace Framework.Settings.Repositories;

public interface ISettingValueRecordRepository
{
    Task<SettingValueRecord?> FindAsync(
        string name,
        string providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    );

    Task<List<SettingValueRecord>> FindAllAsync(
        string name,
        string? providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    );

    Task<List<SettingValueRecord>> GetListAsync(
        string[] names,
        string providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    );

    Task<List<SettingValueRecord>> GetListAsync(
        string providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    );

    Task InsertAsync(SettingValueRecord setting, CancellationToken cancellationToken = default);

    Task UpdateAsync(SettingValueRecord setting, CancellationToken cancellationToken = default);

    Task DeleteAsync(IReadOnlyCollection<SettingValueRecord> settings, CancellationToken cancellationToken = default);
}
