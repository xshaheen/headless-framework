using Framework.Permissions.Entities;

namespace Framework.Permissions.Definitions;

public interface IPermissionGroupDefinitionRecordRepository
{
    Task<List<PermissionGroupDefinitionRecord>> GetListAsync();
}
