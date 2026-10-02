// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.DependencyInjection;

namespace Headless.UnitOfWork;

/// <summary>
/// Registers the SQLite unit-of-work provider.
/// </summary>
[PublicAPI]
public static class SetupSqliteUnitOfWork
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Adds the singleton unit-of-work factory the Microsoft.Data.Sqlite helpers (<c>BeginAsync(connection)</c>,
        /// <c>Enlist(connection, transaction)</c>, <c>RunAsync(connection, …)</c>) run on.
        /// </summary>
        /// <remarks>
        /// The provider needs no service of its own beyond the core factory, so this registers
        /// <see cref="SetupUnitOfWork.AddUnitOfWork" /> only; it exists so hosts declare the provider they enlist
        /// through. Idempotent.
        /// </remarks>
        /// <returns>The same <see cref="IServiceCollection" /> for chaining.</returns>
        public IServiceCollection AddSqliteUnitOfWork()
        {
            return services.AddUnitOfWork();
        }
    }
}
