// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Settings.Entities;

namespace Headless.Settings.Repositories;

/// <summary>Persistence contract for <see cref="SettingDefinitionRecord"/> aggregates.</summary>
public interface ISettingDefinitionRecordRepository
{
    /// <summary>Returns all setting definition records from the data source.</summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A list of all <see cref="SettingDefinitionRecord"/> instances currently persisted.</returns>
    Task<List<SettingDefinitionRecord>> GetListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically inserts new records, updates changed records, and deletes removed records in a single batch.
    /// </summary>
    /// <param name="addedRecords">Records to insert.</param>
    /// <param name="changedRecords">Records whose data has changed and must be updated.</param>
    /// <param name="deletedRecords">Records that must be removed from the data source.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task SaveAsync(
        List<SettingDefinitionRecord> addedRecords,
        List<SettingDefinitionRecord> changedRecords,
        List<SettingDefinitionRecord> deletedRecords,
        CancellationToken cancellationToken = default
    );
}
