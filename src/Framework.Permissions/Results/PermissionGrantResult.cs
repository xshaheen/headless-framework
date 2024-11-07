namespace Framework.Permissions.Results;

public sealed record PermissionGrantResult(PermissionGrantStatus Status, string? ProviderKey = null)
{
    public static PermissionGrantResult Prohibited { get; } = new(PermissionGrantStatus.Prohibited);

    public static PermissionGrantResult Granted { get; } = new(PermissionGrantStatus.Granted);

    public static PermissionGrantResult Undefined { get; } = new(PermissionGrantStatus.Undefined);
}
