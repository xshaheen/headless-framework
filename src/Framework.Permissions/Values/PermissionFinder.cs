using Framework.Permissions.Models;
using Framework.Permissions.Results;
using Framework.Permissions.ValueProviders;

namespace Framework.Permissions.Values;

public interface IPermissionFinder
{
    Task<List<IsGrantedResponse>> IsGrantedAsync(List<IsGrantedRequest> requests);
}

public sealed class PermissionFinder(IPermissionManager permissionManager) : IPermissionFinder
{
    public async Task<List<IsGrantedResponse>> IsGrantedAsync(List<IsGrantedRequest> requests)
    {
        var result = new List<IsGrantedResponse>();

        foreach (var item in requests)
        {
            var providers = await permissionManager.GetAsync(
                item.PermissionNames,
                UserPermissionValueProvider.ProviderName,
                item.UserId.ToString()
            );

            var isGrantedResponse = new IsGrantedResponse
            {
                UserId = item.UserId,
                Permissions = providers.Result.ToDictionary(x => x.Name, x => x.IsGranted, StringComparer.Ordinal),
            };

            result.Add(isGrantedResponse);
        }

        return result;
    }
}

[PublicAPI]
public static class PermissionFinderExtensions
{
    public static async Task<bool> IsGrantedAsync(
        this IPermissionFinder permissionFinder,
        Guid userId,
        string permissionName
    )
    {
        return await permissionFinder.IsGrantedAsync(userId, [permissionName]);
    }

    public static async Task<bool> IsGrantedAsync(
        this IPermissionFinder permissionFinder,
        Guid userId,
        string[] permissionNames
    )
    {
        var isGrantedResponses = await permissionFinder.IsGrantedAsync(
            [new() { UserId = userId, PermissionNames = permissionNames }]
        );

        return isGrantedResponses.Exists(x =>
            x.UserId == userId
            && x.Permissions.All(p => permissionNames.Contains(p.Key, StringComparer.Ordinal) && p.Value)
        );
    }
}
