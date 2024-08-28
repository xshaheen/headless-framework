using Framework.Permissions.Permissions.Checkers;

namespace Framework.Permissions.Permissions.Values;

public interface IPermissionValueProvider
{
    string Name { get; }

    Task<PermissionGrantResult> GetResultAsync(PermissionValueCheckContext context);

    Task<MultiplePermissionGrantResult> GetResultAsync(PermissionValuesCheckContext context);
}

public abstract class PermissionValueProvider(IPermissionStore store) : IPermissionValueProvider
{
    public abstract string Name { get; }

    protected IPermissionStore PermissionStore { get; } = store;

    public abstract Task<PermissionGrantResult> GetResultAsync(PermissionValueCheckContext context);

    public abstract Task<MultiplePermissionGrantResult> GetResultAsync(PermissionValuesCheckContext context);
}
