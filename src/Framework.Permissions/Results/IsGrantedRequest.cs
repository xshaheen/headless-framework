namespace Framework.Permissions.Results;

public sealed class IsGrantedRequest
{
    public required Guid UserId { get; init; }

    public required string[] PermissionNames { get; init; }
}
