namespace Framework.Permissions.Results;

public sealed class IsGrantedResponse
{
    public required Guid UserId { get; init; }

    public required Dictionary<string, bool> Permissions { get; init; }
}
