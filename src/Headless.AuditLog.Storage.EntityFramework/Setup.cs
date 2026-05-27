// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.AuditLog;
using Headless.AuditLog.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

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

    private sealed class EntityFrameworkAuditLogOptionsExtension(Type dbContextType) : IAuditLogStorageOptionsExtension
    {
        public void AddServices(IServiceCollection services)
        {
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
