// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Messaging.Persistence;
using Headless.Messaging.Runtime;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Headless.Messaging.Storage.PostgreSql;

/// <summary>
/// PostgreSQL-specific configuration for the messaging outbox storage backend.
/// Extends <c>PostgreSqlEntityFrameworkMessagingOptions</c> with the raw-ADO connection
/// settings used when the storage is not wired through an EF Core <c>DbContext</c>.
/// </summary>
[PublicAPI]
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
    /// Gets or sets the command timeout applied to schema-initialization DDL — the
    /// <c>CREATE INDEX CONCURRENTLY</c> / <c>DROP INDEX CONCURRENTLY</c> builds, the
    /// <c>CREATE EXTENSION</c> probe, and the advisory-lock waits that gate them.
    /// <para>
    /// These operations can legitimately run for minutes-to-hours on a large table, far longer than
    /// the OLTP <c>MessagingOptions.CommandTimeout</c> (~30s) used for query/write paths. On timeout
    /// PostgreSQL marks a <c>CONCURRENTLY</c> index <c>INVALID</c> and the next boot must repair it, so
    /// this value is deliberately decoupled from the OLTP budget.
    /// </para>
    /// <para>
    /// Default <see langword="null" /> means <b>no timeout</b> (wait indefinitely): the DDL runs with an
    /// Npgsql <c>CommandTimeout</c> of <c>0</c>. Set a finite value to cap startup DDL. <see cref="TimeSpan.Zero"/>
    /// is also treated as "no timeout". A negative value is rejected at validation time.
    /// </para>
    /// </summary>
    public TimeSpan? DdlCommandTimeout { get; set; }

    /// <summary>
    /// Creates an Npgsql connection from the configured data source.
    /// </summary>
    internal NpgsqlConnection CreateConnection()
    {
        if (DataSource is not null)
        {
            return DataSource.CreateConnection();
        }

        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            throw new InvalidOperationException(
                "PostgreSQL messaging storage requires either a DataSource or ConnectionString. "
                    + "Configure via UsePostgreSql(connectionString) or UsePostgreSql(options => options.ConnectionString = ...)"
            );
        }

        return new NpgsqlConnection(ConnectionString);
    }
}

internal sealed class PostgreSqlOptionsValidator : AbstractValidator<PostgreSqlOptions>
{
    public PostgreSqlOptionsValidator()
    {
        RuleFor(x => x)
            .Must(x => x.DataSource is not null || !string.IsNullOrWhiteSpace(x.ConnectionString))
            .WithMessage(
                "PostgreSQL messaging storage requires either a DataSource or ConnectionString. "
                    + "Configure via UsePostgreSql(connectionString) or UsePostgreSql(options => options.ConnectionString = ...)"
            );

        RuleFor(x => x.OwnerColumnMaxLength).GreaterThanOrEqualTo(DataStorageConstants.MinimumOwnerColumnMaxLength);

        // A negative DDL timeout is meaningless; null/Zero already express "no timeout".
        RuleFor(x => x.DdlCommandTimeout!.Value)
            .GreaterThanOrEqualTo(TimeSpan.Zero)
            .When(x => x.DdlCommandTimeout is not null)
            .WithMessage("DdlCommandTimeout must be greater than or equal to zero (zero or null means no timeout).");
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

        if (
            RuntimeTypeInspection.DeclaresFieldOfType<IOutboxBus>(options.DbContextType)
            || RuntimeTypeInspection.DeclaresFieldOfType<IOutboxQueue>(options.DbContextType)
        )
        {
            throw new InvalidOperationException(
                "We detected that you are using IOutboxBus or IOutboxQueue in DbContext, please change the configuration to use the storage extension directly to avoid circular references! eg:  x.UsePostgreSql()"
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

        // Fail loud at configure time when the reflection extraction produced neither a DataSource
        // nor a ConnectionString. Without this the failure surfaces far away as the validator's
        // generic "requires either a DataSource or ConnectionString" message at ValidateOnStart,
        // hiding the real cause (the Npgsql EF Core provider renamed/restructured these properties).
        if (options.DataSource is null && string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            throw new InvalidOperationException(
                "Failed to resolve a DataSource or ConnectionString from the EF Core provider extension "
                    + $"'{extension.GetType().FullName}' for DbContext '{options.DbContextType.FullName}'. The reflected "
                    + $"properties '{nameof(options.DataSource)}'/'{nameof(options.ConnectionString)}' returned null — "
                    + "the Npgsql EF Core provider may have renamed or restructured them."
            );
        }
    }
}
