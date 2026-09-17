// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.DependencyInjection;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.UnitOfWork;

/// <summary>
/// Registers the SQL Server unit-of-work provider.
/// </summary>
[PublicAPI]
public static class SetupSqlServerUnitOfWork
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Adds the scoped unit-of-work manager the SqlClient helpers (<c>BeginAsync(connection)</c>,
        /// <c>Enlist(connection, transaction)</c>, <c>RunAsync(connection, …)</c>) run on.
        /// </summary>
        /// <remarks>
        /// The provider needs no service of its own beyond the core manager, so this registers
        /// <see cref="SetupUnitOfWork.AddUnitOfWork" /> only; it exists so hosts declare the provider they enlist
        /// through. Idempotent.
        /// </remarks>
        /// <returns>The same <see cref="IServiceCollection" /> for chaining.</returns>
        public IServiceCollection AddSqlServerUnitOfWork()
        {
            return services.AddUnitOfWork();
        }
    }
}
