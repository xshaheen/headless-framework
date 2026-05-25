// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.AuditLog;
using Headless.AuditLog.SqlServer;
using Headless.Checks;
using Headless.Hosting.Storage;
using Microsoft.Extensions.DependencyInjection.Extensions;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

[PublicAPI]
public static class SetupAuditLogSqlServer
{
    extension(HeadlessAuditLogSetupBuilder setup)
    {
        public HeadlessAuditLogSetupBuilder UseSqlServer(string connectionString)
        {
            Argument.IsNotNullOrWhiteSpace(connectionString);

            return setup.UseSqlServer(options =>
            {
                options.ConnectionString = connectionString;
            });
        }

        public HeadlessAuditLogSetupBuilder UseSqlServer(Action<SqlServerAuditLogOptions> configure)
        {
            Argument.IsNotNull(configure);

            setup.RegisterExtension(new SqlServerAuditLogOptionsExtension(configure));

            return setup;
        }
    }

    private sealed class SqlServerAuditLogOptionsExtension(Action<SqlServerAuditLogOptions> configure)
        : IStorageOptionsExtension
    {
        public void AddServices(IServiceCollection services)
        {
            services.Configure<SqlServerAuditLogOptions, SqlServerAuditLogOptionsValidator>(configure);
            services.AddInitializerHostedService<SqlServerAuditLogStorageInitializer>();
            services.TryAddSingleton<SqlServerAuditLogWriter>();
            services.TryAddScoped<IAuditLogStore, SqlServerAuditLogStore>();
            services.TryAddSingleton(typeof(IAuditLog<>), typeof(SqlServerAuditLog<>));
            services.TryAddSingleton(typeof(IReadAuditLog<>), typeof(SqlServerReadAuditLog<>));
            services.TryAddSingleton(TimeProvider.System);
            services.TryAddSingleton<IClock, Clock>();
            services.TryAddSingleton<ICurrentTenant, NullCurrentTenant>();
            services.TryAddSingleton<ICurrentUser, NullCurrentUser>();
            services.TryAddSingleton<ICorrelationIdProvider, ActivityCorrelationIdProvider>();
        }
    }
}
