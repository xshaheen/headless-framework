// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.MultiTenancy;

/// <summary>Registration entry point for the typed leaf accessor.</summary>
[PublicAPI]
public static class SetupTypedCurrentTenantInfo
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers <see cref="ICurrentTenantInfo{T}"/>, backed by the base <see cref="ICurrentTenantInfo"/>
        /// accessor and the supplied projection delegate.
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
