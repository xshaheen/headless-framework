// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Core;

namespace Headless.Testing.Helpers;

/// <summary>
/// Mutable <see cref="ICurrentTenant"/> implementation for tests. Allows direct assignment of
/// <see cref="Id"/> and <see cref="Name"/>, or scoped changes via <see cref="Change"/>.
/// </summary>
[PublicAPI]
public sealed class TestCurrentTenant : ICurrentTenant
{
    /// <summary><see langword="true"/> when <see cref="Id"/> is non-null.</summary>
    public bool IsAvailable => Id != null;

    /// <summary>The current tenant identifier, or <see langword="null"/> when no tenant is active.</summary>
    public string? Id { get; set; }

    /// <summary>The current tenant display name, or <see langword="null"/> when no tenant is active.</summary>
    public string? Name { get; set; }

    /// <summary>
    /// Temporarily changes the active tenant to <paramref name="id"/> / <paramref name="name"/>
    /// and returns a disposable that restores the previous values when disposed. Supports nesting:
    /// inner scopes restore to their own outer context rather than to <see langword="null"/>.
    /// </summary>
    /// <param name="id">The tenant identifier to activate for the duration of the scope.</param>
    /// <param name="name">The tenant display name. Defaults to <see langword="null"/>.</param>
    /// <returns>
    /// An <see cref="IDisposable"/> that, when disposed, restores <see cref="Id"/> and
    /// <see cref="Name"/> to their values at the time of this call.
    /// </returns>
    public IDisposable Change(string? id, string? name = null)
    {
        // Capture the previous (Id, Name) BEFORE overwriting so the returned disposable restores the
        // outer scope's tenant rather than nulling it out. Matters for nested Change(...) usage in
        // tests: a child scope must not leak as a tenant clear when it disposes.
        var previousId = Id;
        var previousName = Name;

        Id = id;
        Name = name;

        return DisposableFactory.Create(() =>
        {
            Id = previousId;
            Name = previousName;
        });
    }
}
