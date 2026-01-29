// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.SqlServer;

public class SqlServerOptions : SqlServerEntityMessagingOptions
{
    /// <summary>
    /// Gets or sets the database's connection string that will be used to store database entities.
    /// </summary>
    public required string ConnectionString { get; set; }
}

internal class ConfigureSqlServerOptions(IServiceScopeFactory serviceScopeFactory) : IConfigureOptions<SqlServerOptions>
{
    public void Configure(SqlServerOptions options)
    {
        if (options.DbContextType == null)
        {
            return;
        }

        if (Helper.IsUsingType<IOutboxPublisher>(options.DbContextType))
        {
            throw new InvalidOperationException(
                "We detected that you are using IOutboxPublisher in DbContext, please change the configuration to use the storage extension directly to avoid circular references! eg:  x.UseSqlServer()"
            );
        }

        using var scope = serviceScopeFactory.CreateScope();
        var provider = scope.ServiceProvider;
        using var dbContext = (DbContext)provider.GetRequiredService(options.DbContextType);
        var connectionString = dbContext.Database.GetConnectionString();
        if (string.IsNullOrEmpty(connectionString))
        {
            throw new InvalidOperationException(
                "DbContext returned null or empty connection string. Ensure the DbContext is properly configured."
            );
        }

        options.ConnectionString = connectionString;
    }
}
