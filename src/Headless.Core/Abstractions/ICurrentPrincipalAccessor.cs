// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Claims;
using Headless.Core;

namespace Headless.Abstractions;

/// <summary>
/// Exposes access to the ambient <see cref="ClaimsPrincipal"/> in the current execution context.
/// </summary>
/// <remarks>
/// Low-level infrastructure uses this to supply a principal before invoking application code. Higher-level code should use <see cref="ICurrentUser"/>.
/// </remarks>
public interface ICurrentPrincipalAccessor
{
    /// <summary>
    /// Gets the current <see cref="ClaimsPrincipal"/>, or <see langword="null"/> when no principal is configured.
    /// </summary>
    ClaimsPrincipal? Principal { get; }

    /// <summary>
    /// Temporarily overrides the ambient principal for the lifetime of the returned scope.
    /// </summary>
    /// <param name="principal">
    /// The principal to activate for the current scope, or <see langword="null"/> to clear the override.
    /// </param>
    /// <returns>
    /// A disposable scope handle that restores the previous principal upon disposal.
    /// </returns>
    [MustDisposeResource]
    IDisposable Change(ClaimsPrincipal? principal);
}

/// <summary>
/// Provides a base implementation for <see cref="ICurrentPrincipalAccessor"/> layering an
/// <see cref="AsyncLocal{T}"/> override slot over a fallback principal provider.
/// </summary>
/// <remarks>
/// When the async-local slot is empty, <see cref="Principal"/> delegates to <see cref="GetClaimsPrincipal"/>.
/// </remarks>
public abstract class CurrentPrincipalAccessor : ICurrentPrincipalAccessor
{
    private readonly AsyncLocal<ClaimsPrincipal?> _currentPrincipal = new();

    /// <inheritdoc/>
    public ClaimsPrincipal? Principal => _currentPrincipal.Value ?? GetClaimsPrincipal();

    /// <summary>
    /// Resolves the fallback principal when no explicit override is active in the current asynchronous context.
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
/// Implements <see cref="CurrentPrincipalAccessor"/> using <see cref="Thread.CurrentPrincipal"/> as the fallback source.
/// </summary>
public class ThreadCurrentPrincipalAccessor : CurrentPrincipalAccessor
{
    /// <inheritdoc/>
    protected override ClaimsPrincipal? GetClaimsPrincipal()
    {
        return Thread.CurrentPrincipal as ClaimsPrincipal;
    }
}
