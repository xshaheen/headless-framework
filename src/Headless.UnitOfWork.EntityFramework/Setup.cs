// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.UnitOfWork;
using Microsoft.Extensions.DependencyInjection;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace

namespace Headless.UnitOfWork;

/// <summary>
/// Registers the EF Core unit-of-work provider services.
/// </summary>
[PublicAPI]
public static class SetupEntityFrameworkUnitOfWork
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Adds the EF Core unit-of-work provider: the singleton <see cref="IUnitOfWorkFactory" /> plus the
        /// <c>BeginAsync(db)</c> / <c>Enlist(db, transaction)</c> / <c>RunAsync(db, …)</c> extension members.
        /// </summary>
        /// <remarks>
        /// Idempotent: it only calls <c>AddUnitOfWork()</c> (itself idempotent), so repeated calls and
        /// composition with other provider registrations leave exactly one factory in the container. There
        /// is no interceptor, no options configuration, and no startup gate — the unit of work owns its
        /// commit edge, so nothing needs to observe EF's transaction events.
        /// </remarks>
        /// <returns>The same <see cref="IServiceCollection" /> for chaining.</returns>
        public IServiceCollection AddEntityFrameworkUnitOfWork()
        {
            services.AddUnitOfWork();

            return services;
        }
    }
}
