// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Messaging.Internal;
using Headless.Messaging.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.Storage.SqlServer;

/// <summary>
/// SQL Server-specific configuration for the messaging outbox storage backend.
/// Extends <c>SqlServerEntityFrameworkMessagingOptions</c> with the raw-ADO connection
/// string used when the storage is not wired through an EF Core <c>DbContext</c>.
/// </summary>
public sealed class SqlServerOptions : SqlServerEntityFrameworkMessagingOptions
{
    /// <summary>
    /// Gets or sets the database's connection string that will be used to store database entities.
    /// </summary>
    public string? ConnectionString { get; set; }
}

internal sealed class SqlServerOptionsValidator : AbstractValidator<SqlServerOptions>
{
    public SqlServerOptionsValidator()
    {
        RuleFor(x => x)
            .Must(x => x.DbContextType is not null || !string.IsNullOrWhiteSpace(x.ConnectionString))
            .WithMessage(
                "SQL Server messaging storage requires either a DbContextType or ConnectionString. "
                    + "Configure via UseSqlServer(connectionString) or UseSqlServer(options => options.ConnectionString = ...)"
            );

        RuleFor(x => x.OwnerColumnMaxLength).GreaterThanOrEqualTo(DataStorageConstants.MinimumOwnerColumnMaxLength);
    }
}

internal sealed class ConfigureSqlServerOptions(IServiceScopeFactory serviceScopeFactory)
    : IConfigureOptions<SqlServerOptions>
{
    public void Configure(SqlServerOptions options)
    {
        if (options.DbContextType == null)
        {
            return;
        }

        if (
            Helper.IsUsingType<IOutboxBus>(options.DbContextType)
            || Helper.IsUsingType<IOutboxQueue>(options.DbContextType)
        )
        {
            throw new InvalidOperationException(
                "We detected that you are using IOutboxBus or IOutboxQueue in DbContext, please change the configuration to use the storage extension directly to avoid circular references! eg:  x.UseSqlServer()"
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
