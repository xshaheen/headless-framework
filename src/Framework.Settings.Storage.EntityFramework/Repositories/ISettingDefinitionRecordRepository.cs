using Framework.Settings.Entities;

namespace Framework.Settings.Repositories;

public interface ISettingDefinitionRecordRepository
{
    Task<List<SettingDefinitionRecord>> GetListAsync(CancellationToken cancellationToken = default);

    Task<SettingDefinitionRecord> FindByNameAsync(string name, CancellationToken cancellationToken = default);

    Task InsertManyAsync(List<SettingDefinitionRecord> definitions, CancellationToken cancellationToken = default);

    Task UpdateManyAsync(List<SettingDefinitionRecord> definitions, CancellationToken cancellationToken = default);

    Task DeleteManyAsync(List<SettingDefinitionRecord> definitions, CancellationToken cancellationToken = default);
}
