// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Claims;
using Framework.Core;

namespace Framework.Abstractions;

public interface ICurrentPrincipalAccessor
{
    ClaimsPrincipal? Principal { get; }

    [MustDisposeResource]
    IDisposable Change(ClaimsPrincipal? principal);
}

public abstract class CurrentPrincipalAccessor : ICurrentPrincipalAccessor
{
    private readonly AsyncLocal<ClaimsPrincipal?> _currentPrincipal = new();

    public ClaimsPrincipal? Principal => _currentPrincipal.Value ?? GetClaimsPrincipal();

    protected abstract ClaimsPrincipal? GetClaimsPrincipal();

    [MustDisposeResource]
    public virtual IDisposable Change(ClaimsPrincipal? principal)
    {
        var parent = Principal;
        _currentPrincipal.Value = principal;

        return DisposableFactory.Create(() => _currentPrincipal.Value = parent);
    }
}

public class ThreadCurrentPrincipalAccessor : CurrentPrincipalAccessor
{
    protected override ClaimsPrincipal? GetClaimsPrincipal()
    {
        return Thread.CurrentPrincipal as ClaimsPrincipal;
    }
}
