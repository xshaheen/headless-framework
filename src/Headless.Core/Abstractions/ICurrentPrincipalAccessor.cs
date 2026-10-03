// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Claims;
using Headless.Core;

namespace Headless.Abstractions;

/// <summary>
/// Low-level accessor for the ambient <see cref="ClaimsPrincipal"/> in the current execution
/// context. Framework infrastructure uses this to push a principal (for example, from an HTTP
/// request or a message envelope) before calling into application code. Higher-level code
/// should prefer <see cref="ICurrentUser"/>.
/// </summary>
public interface ICurrentPrincipalAccessor
{
    /// <summary>
    /// Gets the current <see cref="ClaimsPrincipal"/>, or <see langword="null"/> when no principal has
    /// been set for the current execution context.
    /// </summary>
    ClaimsPrincipal? Principal { get; }

    /// <summary>
    /// Temporarily overrides the ambient principal for the duration of the returned scope.
    /// The previous principal is restored automatically when the returned <see cref="IDisposable"/>
    /// is disposed.
    /// </summary>
    /// <param name="principal">
    /// The principal to activate for the current scope, or <see langword="null"/> to remove the override
    /// and fall back to the implementation's default resolution (for example
    /// <see cref="Thread.CurrentPrincipal"/>).
    /// </param>
    /// <returns>
    /// A scope handle that restores the previous principal when disposed.
    /// Always dispose this value — prefer a <see langword="using"/> declaration.
    /// </returns>
    [MustDisposeResource]
    IDisposable Change(ClaimsPrincipal? principal);
}
