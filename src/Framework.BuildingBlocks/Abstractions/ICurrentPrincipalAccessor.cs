// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Claims;
using Framework.BuildingBlocks.Helpers.System;

namespace Framework.BuildingBlocks.Abstractions;

public interface ICurrentPrincipalAccessor
{
    ClaimsPrincipal Principal { get; }

    IDisposable Change(ClaimsPrincipal principal);
}

public abstract class CurrentPrincipalAccessor : ICurrentPrincipalAccessor
{
    private readonly AsyncLocal<ClaimsPrincipal> _currentPrincipal = new();

    public ClaimsPrincipal Principal => _currentPrincipal.Value ?? GetClaimsPrincipal();

    protected abstract ClaimsPrincipal GetClaimsPrincipal();

    public virtual IDisposable Change(ClaimsPrincipal principal)
    {
        var parent = Principal;
        _currentPrincipal.Value = principal;

        return Disposable.Create(
            (_currentPrincipal, parent),
            static state =>
            {
                var (currentPrincipal, parent) = state;
                currentPrincipal.Value = parent;
            }
        );
    }
}

public class ThreadCurrentPrincipalAccessor : CurrentPrincipalAccessor
{
    protected override ClaimsPrincipal GetClaimsPrincipal()
    {
        return Thread.CurrentPrincipal as ClaimsPrincipal
            ?? throw new InvalidOperationException("Thread.CurrentPrincipal is null or not a ClaimsPrincipal.");
    }
}
