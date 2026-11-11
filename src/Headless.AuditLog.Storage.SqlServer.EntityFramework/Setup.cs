// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.AuditLog.SqlServer.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.AuditLog;

/// <summary>Configures Entity Framework audit log storage on SQL Server.</summary>
[PublicAPI]
public static class SetupAuditLogSqlServerEntityFramework
{
    extension(HeadlessAuditLogSetupBuilder setup)
    {
        /// <summary>
        /// Configures the audit log to persist entries through <typeparamref name="TContext"/>, an EF Core context on
        /// SQL Server. It registers everything <c>UseEntityFramework&lt;TContext&gt;()</c> does, with a retention
        /// purge that passes the append-only trigger.
        /// </summary>
        /// <typeparam name="TContext">
        /// The <c>DbContext</c> subclass that owns the audit log table. The context must call
        /// <c>modelBuilder.ConfigureHeadlessAuditLog(this)</c> inside <c>OnModelCreating</c>.
        /// </typeparam>
        /// <returns>The setup builder for chaining.</returns>
        /// <remarks>
        /// The application's migrations own the table: make it append-only with
        /// <c>migrationBuilder.AddHeadlessAuditLogAppendOnly(...)</c>. A startup gate refuses a context that runs on
        /// another database.
        /// </remarks>
        public HeadlessAuditLogSetupBuilder UseSqlServerEntityFramework<TContext>()
            where TContext : DbContext
        {
            setup.RegisterExtension(
                new SetupAuditLogEntityFramework.EntityFrameworkAuditLogOptionsExtension(
                    typeof(TContext),
                    StorageProvider.SqlServer,
                    _AddSqlServerServices
                )
            );

            return setup;
        }
    }

    private static void _AddSqlServerServices(IServiceCollection services, Type dbContextType)
    {
        services.TryAddSingleton(
            typeof(IAuditLogRetention<>).MakeGenericType(dbContextType),
            typeof(SqlServerEfAuditLogRetention<>).MakeGenericType(dbContextType)
        );
    }
}
