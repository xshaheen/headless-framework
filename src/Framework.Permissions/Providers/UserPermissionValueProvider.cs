using System.Security.Claims;
using Framework.Arguments;
using Framework.Permissions.Permissions.Checkers;
using Framework.Permissions.Permissions.Values;

namespace Framework.Permissions.Providers;

[PublicAPI]
public sealed class UserPermissionValueProvider(IPermissionStore store) : PermissionValueProvider(store)
{
    public const string ProviderName = "User";

    public override string Name => ProviderName;

    public override async Task<PermissionGrantResult> GetResultAsync(PermissionValueCheckContext context)
    {
        var userId = context.Principal?.GetUserId();

        return userId is null
            ? PermissionGrantResult.Undefined
            : await PermissionStore.IsGrantedAsync(context.Permission.Name, Name, userId)
                ? PermissionGrantResult.Granted
                : PermissionGrantResult.Undefined;
    }

    public override async Task<MultiplePermissionGrantResult> GetResultAsync(PermissionValuesCheckContext context)
    {
        Argument.IsNotNullOrEmpty(context.Permissions);

        var permissionNames = context.Permissions.Select(x => x.Name).Distinct(StringComparer.Ordinal).ToArray();
        var userId = context.Principal?.GetUserId();

        return userId is null
            ? new MultiplePermissionGrantResult(permissionNames)
            : await PermissionStore.IsGrantedAsync(permissionNames, Name, userId);
    }
}
