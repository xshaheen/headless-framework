// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Claims;

namespace Headless.Context;

/// <summary>
/// Base class for <see cref="ICurrentPrincipalAccessor"/> implementations that layer an
/// <see cref="AsyncLocal{T}"/> override slot on top of an implementation-defined fallback
/// principal (for example, <see cref="Thread.CurrentPrincipal"/>).
/// </summary>
/// <remarks>
/// The <see cref="AsyncLocal{T}"/> slot stores only the explicitly overridden principal.
/// When the slot is empty, <see cref="Principal"/> delegates to <see cref="GetClaimsPrincipal"/>.
/// <see cref="Change"/> captures and restores the raw slot value — not the resolved principal —
/// so that disposing a scope re-exposes the fallback rather than permanently shadowing it.
/// </remarks>
public abstract class CurrentPrincipalAccessor : ICurrentPrincipalAccessor
{
    private readonly AsyncLocal<ClaimsPrincipal?> _currentPrincipal = new();

    /// <inheritdoc/>
    public ClaimsPrincipal? Principal => _currentPrincipal.Value ?? GetClaimsPrincipal();

    /// <summary>
    /// Returns the fallback principal when no explicit override is active in the current
    /// async context. Derived classes resolve the principal from their specific source
    /// (for example, <see cref="Thread.CurrentPrincipal"/> or an HTTP context).
    /// </summary>
    /// <returns>The fallback <see cref="ClaimsPrincipal"/>, or <see langword="null"/>.</returns>
    protected abstract ClaimsPrincipal? GetClaimsPrincipal();

    /// <inheritdoc/>
    [MustDisposeResource]
    public virtual IDisposable Change(ClaimsPrincipal? principal)
    {
        // Capture the raw AsyncLocal slot (not the resolved Principal). Restoring the resolved
        // value would write the GetClaimsPrincipal() fallback back into the slot and permanently
        // shadow it; capturing the raw value restores null so the fallback is consulted again.
        var parent = _currentPrincipal.Value;
        _currentPrincipal.Value = principal;

        // Principal switching sits on per-request and per-message paths: the state-taking overload with a
        // static lambda keeps the restore to a single allocation, unlike a closure over the raw slot.
        return DisposableFactory.Create(
            (Slot: _currentPrincipal, Parent: parent),
            static scope => scope.Slot.Value = scope.Parent
        );
    }
}

/// <summary>
/// <see cref="CurrentPrincipalAccessor"/> implementation that uses <see cref="Thread.CurrentPrincipal"/>
/// as the fallback when no async-local override is active. Suitable for non-ASP.NET hosted environments
/// (console apps, worker services) that rely on the thread-static principal.
/// </summary>
public class ThreadCurrentPrincipalAccessor : CurrentPrincipalAccessor
{
    /// <inheritdoc/>
    protected override ClaimsPrincipal? GetClaimsPrincipal()
    {
        return Thread.CurrentPrincipal as ClaimsPrincipal;
    }
}
