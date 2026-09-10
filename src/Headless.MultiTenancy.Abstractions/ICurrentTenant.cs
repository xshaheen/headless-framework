// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.MultiTenancy;

/// <summary>
/// Provides access to the current tenant context for the ambient async execution scope.
/// Implementations maintain a scoped tenant identity that can be temporarily overridden
/// via <see cref="Change"/>.
/// </summary>
public interface ICurrentTenant
{
    /// <summary>
    /// Gets a value indicating whether a tenant identifier has been set in the current scope.
    /// Returns <see langword="false"/> when no tenant context is active (for example, in background jobs
    /// or admin operations that run outside a tenant boundary).
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Gets the current tenant identifier, or <see langword="null"/> when no tenant context is active.
    /// </summary>
    string? Id { get; }

    /// <summary>
    /// Gets the display name of the current tenant, or <see langword="null"/> when no tenant context is active
    /// or when only the identifier was set without a name.
    /// </summary>
    string? Name { get; }

    /// <summary>
    /// Temporarily overrides the ambient tenant context for the duration of the returned scope.
    /// The previous tenant context is restored automatically when the returned <see cref="IDisposable"/>
    /// is disposed.
    /// </summary>
    /// <param name="id">The tenant identifier to activate, or <see langword="null"/> to explicitly clear the tenant context.</param>
    /// <param name="name">The optional display name of the tenant.</param>
    /// <returns>
    /// A scope handle that restores the previous tenant context when disposed.
    /// Always dispose this value — prefer a <see langword="using"/> declaration.
    /// </returns>
    [MustDisposeResource]
    IDisposable Change(string? id, string? name = null);
}
