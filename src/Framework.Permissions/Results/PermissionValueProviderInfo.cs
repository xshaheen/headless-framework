using Framework.Kernel.Checks;

namespace Framework.Permissions.Results;

public sealed class PermissionValueProviderInfo(string name, string key)
{
    public string Name { get; } = Argument.IsNotNull(name);

    public string Key { get; } = Argument.IsNotNull(key);
}
