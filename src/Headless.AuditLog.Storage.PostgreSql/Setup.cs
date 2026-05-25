// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.AuditLog;
using Headless.AuditLog.PostgreSql;
using Headless.Checks;
using Headless.Hosting.Storage;
using Microsoft.Extensions.DependencyInjection.Extensions;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

[PublicAPI]
public static class SetupAuditLogPostgreSql
{
    extension(HeadlessAuditLogSetupBuilder setup)
    {
        public HeadlessAuditLogSetupBuilder UsePostgreSql(string connectionString)
        {
            Argument.IsNotNullOrWhiteSpace(connectionString);

            return setup.UsePostgreSql(options =>
            {
                options.ConnectionString = connectionString;
            });
        }

        public HeadlessAuditLogSetupBuilder UsePostgreSql(Action<PostgreSqlAuditLogOptions> configure)
        {
            Argument.IsNotNull(configure);

            setup.RegisterExtension(new PostgreSqlAuditLogOptionsExtension(configure));

            return setup;
        }
    }

    private sealed class PostgreSqlAuditLogOptionsExtension(Action<PostgreSqlAuditLogOptions> configure)
        : IStorageOptionsExtension
    {
        public void AddServices(IServiceCollection services)
        {
            services.Configure<PostgreSqlAuditLogOptions, PostgreSqlAuditLogOptionsValidator>(configure);
            services.AddInitializerHostedService<PostgreSqlAuditLogStorageInitializer>();
            services.TryAddSingleton<PostgreSqlAuditLogWriter>();
            services.TryAddScoped<IAuditLogStore, PostgreSqlAuditLogStore>();
            services.TryAddSingleton(typeof(IAuditLog<>), typeof(PostgreSqlAuditLog<>));
            services.TryAddSingleton(typeof(IReadAuditLog<>), typeof(PostgreSqlReadAuditLog<>));
            services.TryAddSingleton(TimeProvider.System);
            services.TryAddSingleton<IClock, Clock>();
            services.TryAddSingleton<ICurrentTenant, NullCurrentTenant>();
            services.TryAddSingleton<ICurrentUser, NullCurrentUser>();
            services.TryAddSingleton<ICorrelationIdProvider, ActivityCorrelationIdProvider>();
        }
    }
}
