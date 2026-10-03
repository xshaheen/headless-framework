// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Core;
using Headless.MultiTenancy;
using Headless.Primitives;

namespace Headless.Abstractions;

/// <summary>
/// A no-op <see cref="ICurrentTenant"/> implementation that always reports no active tenant.
/// Useful as a default/fallback registration in contexts where multi-tenancy is not required.
/// </summary>
public sealed class NullCurrentTenant : ICurrentTenant
{
    /// <inheritdoc/>
    public bool IsAvailable => false;

    /// <inheritdoc/>
    public string? Id => null;

    /// <inheritdoc/>
    public string? Name => null;

    /// <inheritdoc/>
    public IDisposable Change(string? id, string? name = null)
    {
        return DisposableFactory.Empty;
    }
}
