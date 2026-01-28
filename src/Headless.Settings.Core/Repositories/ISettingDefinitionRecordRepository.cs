// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Settings.Entities;

namespace Headless.Settings.Repositories;

public interface ISettingDefinitionRecordRepository
{
    Task<List<SettingDefinitionRecord>> GetListAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(
        List<SettingDefinitionRecord> addedRecords,
        List<SettingDefinitionRecord> changedRecords,
        List<SettingDefinitionRecord> deletedRecords,
        CancellationToken cancellationToken = default
    );
}
