// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Headless.Messaging.Persistence;
using Headless.Messaging.SqlServer;
using Headless.Messaging.SqlServer.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

public static class MessagesSqlServerSetup
{
    extension(MessagingSetupBuilder setup)
    {
        public MessagingSetupBuilder UseSqlServer(string connectionString)
        {
            Argument.IsNotNullOrWhiteSpace(connectionString);

            return setup.UseSqlServer(opt =>
            {
                opt.ConnectionString = connectionString;
            });
        }

        public MessagingSetupBuilder UseSqlServer(Action<SqlServerOptions> configure)
        {
            Argument.IsNotNull(configure);

            configure += x => x.Version = setup.Options.Version;

            setup.RegisterExtension(new SqlServerMessagesOptionsExtension(configure));

            return setup;
        }

        public MessagingSetupBuilder UseEntityFramework<TContext>()
            where TContext : DbContext
        {
            return setup.UseEntityFramework<TContext>(_ => { });
        }

        public MessagingSetupBuilder UseEntityFramework<TContext>(
            Action<SqlServerEntityFrameworkMessagingOptions> configure
        )
            where TContext : DbContext
        {
            Argument.IsNotNull(configure);

            setup.RegisterExtension(
                new SqlServerMessagesOptionsExtension(x =>
                {
                    configure(x);
                    x.Version = setup.Options.Version;
                    x.DbContextType = typeof(TContext);
                })
            );

            return setup;
        }
    }

    private sealed class SqlServerMessagesOptionsExtension(Action<SqlServerOptions> configure)
        : IMessagesOptionsExtension
    {
        public void AddServices(IServiceCollection services)
        {
            services.AddSingleton(new MessageStorageMarkerService("SqlServer"));

            services.AddSingleton<DiagnosticProcessorObserver>();
            services.AddTransient<IOutboxTransaction, SqlServerOutboxTransaction>();
            services.AddSingleton<IDataStorage, SqlServerDataStorage>();
            services.AddSingleton<IStorageInitializer, SqlServerStorageInitializer>();
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IProcessingServer, DiagnosticRegister>());

            services.Configure<SqlServerOptions, SqlServerOptionsValidator>(configure);
            services.AddSingleton<IConfigureOptions<SqlServerOptions>, ConfigureSqlServerOptions>();
        }
    }
}
