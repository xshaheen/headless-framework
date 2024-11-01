using Framework.Permissions.Entities;

namespace Framework.Permissions.Definitions;

public interface IPermissionDefinitionRecordRepository
{
    Task<PermissionDefinitionRecord> FindByNameAsync(string name, CancellationToken cancellationToken = default);

    Task<List<PermissionDefinitionRecord>> GetListAsync();
}
