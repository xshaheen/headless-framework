// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Checks;
using Framework.Messages;
using Framework.Messages.Configuration;
using Framework.Messages.Diagnostics;
using Framework.Messages.Internal;
using Framework.Messages.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

public static class MessagesSqlServerSetup
{
    extension(CapOptions options)
    {
        public CapOptions UseSqlServer(string connectionString)
        {
            return options.UseSqlServer(opt =>
            {
                opt.ConnectionString = connectionString;
            });
        }

        public CapOptions UseSqlServer(Action<SqlServerOptions> configure)
        {
            Argument.IsNotNull(configure);

            configure += x => x.Version = options.Version;

            options.RegisterExtension(new SqlServerMessagesOptionsExtension(configure));

            return options;
        }

        public CapOptions UseEntityFramework<TContext>()
            where TContext : DbContext
        {
            return options.UseEntityFramework<TContext>(opt => { });
        }

        public CapOptions UseEntityFramework<TContext>(Action<EfOptions> configure)
            where TContext : DbContext
        {
            Argument.IsNotNull(configure);

            options.RegisterExtension(
                new SqlServerMessagesOptionsExtension(x =>
                {
                    configure(x);
                    x.Version = options.Version;
                    x.DbContextType = typeof(TContext);
                })
            );

            return options;
        }
    }

    private sealed class SqlServerMessagesOptionsExtension(Action<SqlServerOptions> configure)
        : IMessagesOptionsExtension
    {
        public void AddServices(IServiceCollection services)
        {
            services.AddSingleton(new CapStorageMarkerService("SqlServer"));

            services.AddSingleton<DiagnosticProcessorObserver>();
            services.AddSingleton<IDataStorage, SqlServerDataStorage>();
            services.AddSingleton<IStorageInitializer, SqlServerStorageInitializer>();
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IProcessingServer, DiagnosticRegister>());

            services.Configure(configure);
            services.AddSingleton<IConfigureOptions<SqlServerOptions>, ConfigureSqlServerOptions>();
        }
    }
}
