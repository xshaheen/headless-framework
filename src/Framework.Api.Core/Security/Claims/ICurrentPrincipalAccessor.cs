using System.Security.Claims;
using Framework.BuildingBlocks.Helpers;
using Microsoft.AspNetCore.Http;

namespace Framework.Api.Core.Security.Claims;

public interface ICurrentPrincipalAccessor
{
    ClaimsPrincipal Principal { get; }

    IDisposable Change(ClaimsPrincipal principal);
}

public abstract class CurrentPrincipalAccessor : ICurrentPrincipalAccessor
{
    public ClaimsPrincipal Principal => _currentPrincipal.Value ?? GetClaimsPrincipal();

    private readonly AsyncLocal<ClaimsPrincipal> _currentPrincipal = new();

    protected abstract ClaimsPrincipal GetClaimsPrincipal();

    public virtual IDisposable Change(ClaimsPrincipal principal)
    {
        var parent = Principal;
        _currentPrincipal.Value = principal;

        return new DisposeAction<(AsyncLocal<ClaimsPrincipal>, ClaimsPrincipal)>(
            static state =>
            {
                var (currentPrincipal, parent) = state;
                currentPrincipal.Value = parent;
            },
            (_currentPrincipal, parent)
        );
    }
}

public class ThreadCurrentPrincipalAccessor : CurrentPrincipalAccessor
{
    protected override ClaimsPrincipal GetClaimsPrincipal()
    {
        return (Thread.CurrentPrincipal as ClaimsPrincipal)!;
    }
}

public class HttpContextCurrentPrincipalAccessor(IHttpContextAccessor accessor) : ThreadCurrentPrincipalAccessor
{
    protected override ClaimsPrincipal GetClaimsPrincipal()
    {
        return accessor.HttpContext?.User ?? base.GetClaimsPrincipal();
    }
}
