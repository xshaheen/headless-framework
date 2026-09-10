// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.MultiTenancy;

/// <summary>
/// Opt-in typed leaf accessor exposing an app-defined <see cref="TenantInfo"/> subclass view (R10).
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

/// <summary>
/// Default <see cref="ICurrentTenantInfo{T}"/>: downcasts when the base accessor already returned
/// <typeparamref name="T"/> (the fast path — happens when this call's resolution was a cache miss and
/// the store returned the subtype directly), otherwise invokes the app-supplied projection delegate,
/// which may re-hydrate from the store itself (R13) since the cache only ever holds the base shape.
/// </summary>
internal sealed class TypedCurrentTenantInfo<T>(
    ICurrentTenantInfo baseAccessor,
    Func<TenantInfo, CancellationToken, Task<T>> projection
) : ICurrentTenantInfo<T>
    where T : TenantInfo
{
    public async Task<T?> GetAsync(CancellationToken cancellationToken = default)
    {
        var baseInfo = await baseAccessor.GetAsync(cancellationToken).ConfigureAwait(false);

        if (baseInfo is null)
        {
            return null;
        }

        if (baseInfo is T typed)
        {
            return typed;
        }

        return await projection(baseInfo, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Registration entry point for the typed leaf accessor.</summary>
[PublicAPI]
public static class SetupTypedCurrentTenantInfo
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers <see cref="ICurrentTenantInfo{T}"/>, backed by the base <see cref="ICurrentTenantInfo"/>
        /// accessor and the supplied projection delegate (R10).
        /// </summary>
        /// <typeparam name="T">The app-defined <see cref="TenantInfo"/> subclass.</typeparam>
        /// <param name="projection">
        /// Builds <typeparamref name="T"/> from the base <see cref="TenantInfo"/> shape when the base
        /// accessor did not already return an instance of <typeparamref name="T"/>. Free to re-fetch
        /// from an app-owned store to populate subclass-only fields.
        /// </param>
        /// <returns>The same service collection, to allow chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="projection"/> is <see langword="null"/>.</exception>
        public IServiceCollection AddTypedCurrentTenantInfo<T>(Func<TenantInfo, CancellationToken, Task<T>> projection)
            where T : TenantInfo
        {
            Argument.IsNotNull(projection);

            services.AddScoped<ICurrentTenantInfo<T>>(sp => new TypedCurrentTenantInfo<T>(
                sp.GetRequiredService<ICurrentTenantInfo>(),
                projection
            ));

            return services;
        }
    }
}
