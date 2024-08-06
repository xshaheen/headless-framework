using System.Security.Claims;
using Framework.Api.Core.Security;
using Framework.Api.Core.Security.Claims;
using Framework.BuildingBlocks.Primitives;

namespace Framework.Api.Core.Abstractions;

public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    UserId? UserId { get; }

    string? UserType { get; }

    AccountId? AccountId { get; }

    IReadOnlyList<string> Roles { get; }

    Claim? FindClaim(string claimType);

    Claim[] FindClaims(string claimType);

    Claim[] GetAllClaims();
}

public sealed class HttpCurrentUser(ICurrentPrincipalAccessor accessor) : ICurrentUser
{
    public bool IsAuthenticated => UserId is not null;

    public UserId? UserId => accessor.Principal.GetUserId();

    public string? UserType => accessor.Principal.GetUserType();

    public AccountId? AccountId => accessor.Principal.GetAccountId();

    public IReadOnlyList<string> Roles => accessor.Principal.GetRoles();

    public Claim? FindClaim(string claimType)
    {
        return accessor.Principal.Claims.FirstOrDefault(c => c.Type == claimType);
    }

    public Claim[] FindClaims(string claimType)
    {
        return accessor.Principal.Claims.Where(c => c.Type == claimType).ToArray();
    }

    public Claim[] GetAllClaims()
    {
        return accessor.Principal.Claims.ToArray();
    }
}
