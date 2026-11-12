// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.AuditLog.PostgreSql.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.AuditLog;

/// <summary>Configures Entity Framework audit log storage on PostgreSQL.</summary>
[PublicAPI]
public static class SetupAuditLogPostgreSqlEntityFramework
{
    extension(HeadlessAuditLogSetupBuilder setup)
    {
        /// <summary>
        /// Configures the audit log to persist entries through <typeparamref name="TContext"/>, an EF Core context on
        /// PostgreSQL. It registers everything <c>UseEntityFramework&lt;TContext&gt;()</c> does, with a retention purge
        /// that passes the append-only trigger (or runs through the owner's function, with
        /// <see cref="AuditLogPurgeAuthority.Owner"/>), and <see cref="IAuditLogPartitions{TContext}"/> for a table
        /// partitioned by month.
        /// </summary>
        /// <typeparam name="TContext">
        /// The <c>DbContext</c> subclass that owns the audit log table. The context must call
        /// <c>modelBuilder.ConfigureHeadlessAuditLog(this)</c> inside <c>OnModelCreating</c>.
        /// </typeparam>
        /// <returns>The setup builder for chaining.</returns>
        /// <remarks>
        /// The application's migrations own the table: make it append-only or partitioned with
        /// <c>migrationBuilder.AddHeadlessAuditLogAppendOnly(...)</c> and
        /// <c>migrationBuilder.AddHeadlessAuditLogPartitions(...)</c>. A startup gate refuses a context that runs on
        /// another database.
        /// </remarks>
        public HeadlessAuditLogSetupBuilder UsePostgreSqlEntityFramework<TContext>()
            where TContext : DbContext
        {
            setup.RegisterExtension(
                new SetupAuditLogEntityFramework.EntityFrameworkAuditLogOptionsExtension(
                    typeof(TContext),
                    StorageProvider.PostgreSql,
                    _AddPostgreSqlServices
                )
            );

            return setup;
        }
    }

    private static void _AddPostgreSqlServices(IServiceCollection services, Type dbContextType)
    {
        services.TryAddSingleton(
            typeof(IAuditLogRetention<>).MakeGenericType(dbContextType),
            typeof(PostgreSqlEfAuditLogRetention<>).MakeGenericType(dbContextType)
        );
        services.TryAddSingleton(
            typeof(IAuditLogPartitions<>).MakeGenericType(dbContextType),
            typeof(PostgreSqlEfAuditLogPartitions<>).MakeGenericType(dbContextType)
        );
    }
}
