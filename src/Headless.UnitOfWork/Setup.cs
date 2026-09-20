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
        /// Adds the singleton <see cref="IUnitOfWorkFactory" />.
        /// </summary>
        /// <remarks>
        /// Idempotent: repeated calls register the factory at most once. Consumer packages
        /// (<c>Headless.EntityFramework</c>, <c>Headless.Messaging.Core</c>, <c>Headless.Jobs.Core</c>) call
        /// this internally, so exactly one registration exists regardless of which setup the host invokes
        /// first. The factory holds no per-scope state, so any service — a singleton or hosted service included —
        /// may take it directly.
        /// </remarks>
        /// <returns>The same <see cref="IServiceCollection" /> for chaining.</returns>
        public IServiceCollection AddUnitOfWork()
        {
            services.TryAddSingleton<IUnitOfWorkFactory, UnitOfWorkFactory>();

            return services;
        }
    }
}
