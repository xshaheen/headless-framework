// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.UnitOfWork;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace

namespace Headless.UnitOfWork;

/// <summary>
/// Registers the unit-of-work services.
/// </summary>
[PublicAPI]
public static class SetupUnitOfWork
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Adds the scoped <see cref="IUnitOfWorkManager" />.
        /// </summary>
        /// <remarks>
        /// Idempotent: repeated calls register the manager at most once. Consumer packages
        /// (<c>Headless.EntityFramework</c>, <c>Headless.Messaging.Core</c>, <c>Headless.Jobs.Core</c>) call
        /// this internally, so exactly one registration exists regardless of which setup the host invokes
        /// first. The manager is scoped: resolve it (or a scoped facade over it) from a scope, never from the
        /// root provider.
        /// </remarks>
        /// <returns>The same <see cref="IServiceCollection" /> for chaining.</returns>
        public IServiceCollection AddUnitOfWork()
        {
            services.TryAddScoped<IUnitOfWorkManager, UnitOfWorkManager>();

            return services;
        }

        /// <summary>
        /// Adds the scoped <see cref="IUnitOfWorkManager" /> and registers <typeparamref name="TProvider" /> as
        /// the source of one unit-of-work capability, resolvable through
        /// <see cref="IUnitOfWork.GetFeature{TFeature}" />.
        /// </summary>
        /// <remarks>
        /// Idempotent per provider type, so a bridge package's setup can call this without coordinating with
        /// the host. At most one provider may claim a given <see cref="IUnitOfWorkFeatureProvider.FeatureType" />;
        /// a second one for the same feature fails when the scope's manager is created. The provider is scoped,
        /// like the manager that reads it.
        /// </remarks>
        /// <typeparam name="TProvider">The provider implementation to register.</typeparam>
        /// <returns>The same <see cref="IServiceCollection" /> for chaining.</returns>
        public IServiceCollection AddUnitOfWorkFeature<TProvider>()
            where TProvider : class, IUnitOfWorkFeatureProvider
        {
            services.AddUnitOfWork();
            services.TryAddEnumerable(ServiceDescriptor.Scoped<IUnitOfWorkFeatureProvider, TProvider>());

            return services;
        }
    }
}
