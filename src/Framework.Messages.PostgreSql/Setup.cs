// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Checks;
using Framework.Messages;
using Framework.Messages.Configuration;
using Framework.Messages.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

public static class CapOptionsExtensions
{
    extension(CapOptions options)
    {
        public CapOptions UsePostgreSql(string connectionString)
        {
            return options.UsePostgreSql(opt =>
            {
                opt.ConnectionString = connectionString;
            });
        }

        public CapOptions UsePostgreSql(Action<PostgreSqlOptions> configure)
        {
            Argument.IsNotNull(configure);

            configure += x => x.Version = options.Version;

            options.RegisterExtension(new PostgreSqlMessagesOptionsExtension(configure));

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
                new PostgreSqlMessagesOptionsExtension(x =>
                {
                    configure(x);
                    x.Version = options.Version;
                    x.DbContextType = typeof(TContext);
                })
            );

            return options;
        }
    }

    private sealed class PostgreSqlMessagesOptionsExtension(Action<PostgreSqlOptions> configure)
        : IMessagesOptionsExtension
    {
        public void AddServices(IServiceCollection services)
        {
            services.AddSingleton(new CapStorageMarkerService("PostgreSql"));
            services.Configure(configure);
            services.AddSingleton<IConfigureOptions<PostgreSqlOptions>, ConfigurePostgreSqlOptions>();

            services.AddSingleton<IDataStorage, PostgreSqlDataStorage>();
            services.AddSingleton<IStorageInitializer, PostgreSqlStorageInitializer>();
        }
    }
}
