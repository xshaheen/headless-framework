// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Headless.Messaging.PostgreSql;

public sealed class PostgreSqlOptions : PostgreSqlEntityFrameworkMessagingOptions
{
    /// <summary>
    /// Gets or sets the database's connection string that will be used to store database entities.
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// Gets or sets the Npgsql data source that will be used to store database entities.
    /// </summary>
    public NpgsqlDataSource? DataSource { get; set; }

    /// <summary>
    /// Creates an Npgsql connection from the configured data source.
    /// </summary>
    internal NpgsqlConnection CreateConnection()
    {
        return DataSource != null ? DataSource.CreateConnection() : new(ConnectionString);
    }
}

internal sealed class ConfigurePostgreSqlOptions(IServiceScopeFactory serviceScopeFactory)
    : IConfigureOptions<PostgreSqlOptions>
{
    public void Configure(PostgreSqlOptions options)
    {
        if (options.DbContextType == null)
        {
            return;
        }

        if (Helper.IsUsingType<IOutboxPublisher>(options.DbContextType))
        {
            throw new InvalidOperationException(
                "We detected that you are using IOutboxPublisher in DbContext, please change the configuration to use the storage extension directly to avoid circular references! eg:  x.UsePostgreSql()"
            );
        }

        using var scope = serviceScopeFactory.CreateScope();
        var provider = scope.ServiceProvider;
        using var dbContext = (DbContext)provider.GetRequiredService(options.DbContextType);

        var coreOptions = dbContext.GetService<IDbContextOptions>();
        var extension = coreOptions.Extensions.First(x => x.Info.IsDatabaseProvider);
#pragma warning disable REFL003 // The member does not exist
#pragma warning disable REFL017 // Don't use name of wrong member
        options.DataSource =
            extension.GetType().GetProperty(nameof(options.DataSource))?.GetValue(extension) as NpgsqlDataSource;
        if (options.DataSource == null)
        {
            options.ConnectionString =
                extension.GetType().GetProperty(nameof(options.ConnectionString))?.GetValue(extension) as string;
        }
#pragma warning restore REFL017 // Don't use name of wrong member
#pragma warning restore REFL003 // The member does not exist
    }
}
