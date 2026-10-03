// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.MultiTenancy;

/// <summary>
/// Opt-in typed leaf accessor exposing an app-defined <see cref="TenantInfo"/> subclass view.
/// The only pipeline surface that carries a type parameter — the store SPI, cache, and outcome types
/// all stay non-generic per this family's extension-tier design.
/// </summary>
/// <typeparam name="T">The app-defined <see cref="TenantInfo"/> subclass.</typeparam>
[PublicAPI]
public interface ICurrentTenantInfo<T>
    where T : TenantInfo
{
    /// <summary>Loads the typed view of the ambient tenant's info.</summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// The ambient tenant's info as <typeparamref name="T"/>, or <see langword="null"/> per the same
    /// absence rules as <see cref="ICurrentTenantInfo.GetAsync"/>.
    /// </returns>
    Task<T?> GetAsync(CancellationToken cancellationToken = default);
}
