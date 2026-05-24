// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.AuditLog;
using Headless.AuditLog.Internal;
using Headless.Checks;
using Headless.Hosting.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

[PublicAPI]
public static class SetupAuditLog
{
    extension(IServiceCollection services)
    {
        public HeadlessAuditLogBuilder AddHeadlessAuditLog(Action<HeadlessAuditLogSetupBuilder> configure)
        {
            Argument.IsNotNull(configure);

            var setup = new HeadlessAuditLogSetupBuilder(services);
            configure(setup);

            if (setup.Extensions.Count != 1)
            {
                throw new InvalidOperationException(
                    setup.Extensions.Count == 0
                        ? "Headless.AuditLog requires exactly one storage provider. Call one of `UseEntityFramework`, `UsePostgreSql`, or `UseSqlServer`."
                        : "Headless.AuditLog requires exactly one storage provider. Multiple storage providers were configured."
                );
            }

            services.Configure<AuditLogStorageOptions, AuditLogStorageOptionsValidator>(options =>
            {
                options.Schema = setup.StorageOptions.Schema;
                options.TableName = setup.StorageOptions.TableName;
                options.JsonColumnType = setup.StorageOptions.JsonColumnType;
            });

            foreach (var extension in setup.Extensions)
            {
                extension.AddServices(services);
            }

            return new HeadlessAuditLogBuilder(services);
        }
    }
}

[PublicAPI]
public static class SetupAuditLogEntityFramework
{
    extension(HeadlessAuditLogSetupBuilder setup)
    {
        public HeadlessAuditLogSetupBuilder UseEntityFramework<TContext>()
            where TContext : DbContext
        {
            setup.RegisterExtension(new EntityFrameworkAuditLogOptionsExtension(typeof(TContext)));

            return setup;
        }
    }

    private sealed class EntityFrameworkAuditLogOptionsExtension(Type dbContextType) : IStorageOptionsExtension
    {
        public void AddServices(IServiceCollection services)
        {
            services.TryAddScoped<IAuditChangeCapture, EfAuditChangeCapture>();
            services.TryAddScoped<IAuditLogStore, EfAuditLogStore>();
            services.TryAddScoped(typeof(IAuditLog<>).MakeGenericType(dbContextType), typeof(EfAuditLog<>).MakeGenericType(dbContextType));
            services.TryAddSingleton(
                typeof(IReadAuditLog<>).MakeGenericType(dbContextType),
                typeof(EfReadAuditLog<>).MakeGenericType(dbContextType)
            );
            services.TryAddEnumerable(
                ServiceDescriptor.Singleton(
                    typeof(IHostedService),
                    typeof(AuditLogEntityValidationStartupGate<>).MakeGenericType(dbContextType)
                )
            );
        }
    }
}
