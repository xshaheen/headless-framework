// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Checks;
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
    extension(MessagingOptions options)
    {
        public MessagingOptions UseSqlServer(string connectionString)
        {
            Argument.IsNotNullOrWhiteSpace(connectionString);

            return options.UseSqlServer(opt =>
            {
                opt.ConnectionString = connectionString;
            });
        }

        public MessagingOptions UseSqlServer(Action<SqlServerOptions> configure)
        {
            Argument.IsNotNull(configure);

            configure += x => x.Version = options.Version;

            options.RegisterExtension(new SqlServerMessagesOptionsExtension(configure));

            return options;
        }

        public MessagingOptions UseEntityFramework<TContext>()
            where TContext : DbContext
        {
            return options.UseEntityFramework<TContext>(_ => { });
        }

        public MessagingOptions UseEntityFramework<TContext>(Action<SqlServerEntityFrameworkMessagingOptions> configure)
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
            services.AddSingleton(new MessageStorageMarkerService("SqlServer"));

            services.AddSingleton<DiagnosticProcessorObserver>();
            services.AddSingleton<IDataStorage, SqlServerDataStorage>();
            services.AddSingleton<IStorageInitializer, SqlServerStorageInitializer>();
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IProcessingServer, DiagnosticRegister>());

            services.Configure(configure);
            services.AddSingleton<IConfigureOptions<SqlServerOptions>, ConfigureSqlServerOptions>();
            services.AddSingleton<IValidateOptions<SqlServerOptions>, SqlServerOptionsValidator>();
        }
    }
}
