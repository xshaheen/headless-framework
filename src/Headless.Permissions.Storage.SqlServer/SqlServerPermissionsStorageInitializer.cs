// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting.Initialization;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Headless.Permissions.SqlServer;

internal sealed class SqlServerPermissionsStorageInitializer(
    IOptions<SqlServerPermissionsOptions> providerOptions,
    IOptions<PermissionsStorageOptions> storageOptions
) : IHostedLifecycleService, IInitializer
{
    private TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public bool IsInitialized { get; private set; }

    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        _completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            await InitializeAsync(cancellationToken).ConfigureAwait(false);
            IsInitialized = true;
            _completion.TrySetResult();
        }
        catch (Exception ex)
        {
            _completion.TrySetException(ex);
            throw;
        }
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task WaitForInitializationAsync(CancellationToken cancellationToken = default)
    {
        await _completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(providerOptions.Value.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new SqlCommand(_CreateScript(storageOptions.Value), connection)
        {
            CommandTimeout = (int)providerOptions.Value.CommandTimeout.TotalSeconds,
        };
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    internal static string Qualified(PermissionsStorageOptions options, string tableName) =>
        $"[{options.Schema}].[{tableName}]";

    private static string _CreateScript(PermissionsStorageOptions options)
    {
        var grantsTable = Qualified(options, options.PermissionGrantsTableName);
        var definitionsTable = Qualified(options, options.PermissionDefinitionsTableName);
        var groupsTable = Qualified(options, options.PermissionGroupDefinitionsTableName);
        var grantsObject = $"{options.Schema}.{options.PermissionGrantsTableName}";
        var definitionsObject = $"{options.Schema}.{options.PermissionDefinitionsTableName}";
        var groupsObject = $"{options.Schema}.{options.PermissionGroupDefinitionsTableName}";

        return $"""
            BEGIN TRY
                IF NOT EXISTS (SELECT * FROM sys.schemas WHERE name = N'{options.Schema}')
                    EXEC(N'CREATE SCHEMA [{options.Schema}]');
            END TRY
            BEGIN CATCH
                IF ERROR_NUMBER() NOT IN (2714, 1913, 2759) THROW;
            END CATCH;

            BEGIN TRY
                IF OBJECT_ID(N'{groupsObject}', N'U') IS NULL
                BEGIN
                    CREATE TABLE {groupsTable} (
                        [Id] uniqueidentifier NOT NULL,
                        [Name] nvarchar(128) NOT NULL,
                        [DisplayName] nvarchar(256) NOT NULL,
                        [ExtraProperties] nvarchar(max) NOT NULL,
                        CONSTRAINT [PK_{options.PermissionGroupDefinitionsTableName}] PRIMARY KEY CLUSTERED ([Id] ASC)
                    );
                    CREATE UNIQUE NONCLUSTERED INDEX [IX_{options.PermissionGroupDefinitionsTableName}_Name] ON {groupsTable} ([Name] ASC);
                END;
            END TRY
            BEGIN CATCH
                IF ERROR_NUMBER() NOT IN (2714, 1913, 2759) THROW;
            END CATCH;

            BEGIN TRY
                IF OBJECT_ID(N'{definitionsObject}', N'U') IS NULL
                BEGIN
                    CREATE TABLE {definitionsTable} (
                        [Id] uniqueidentifier NOT NULL,
                        [GroupName] nvarchar(128) NOT NULL,
                        [Name] nvarchar(128) NOT NULL,
                        [DisplayName] nvarchar(256) NOT NULL,
                        [IsEnabled] bit NOT NULL,
                        [ParentName] nvarchar(128) NULL,
                        [Providers] nvarchar(128) NULL,
                        [ExtraProperties] nvarchar(max) NOT NULL,
                        CONSTRAINT [PK_{options.PermissionDefinitionsTableName}] PRIMARY KEY CLUSTERED ([Id] ASC)
                    );
                    CREATE NONCLUSTERED INDEX [IX_{options.PermissionDefinitionsTableName}_GroupName] ON {definitionsTable} ([GroupName] ASC);
                    CREATE UNIQUE NONCLUSTERED INDEX [IX_{options.PermissionDefinitionsTableName}_Name] ON {definitionsTable} ([Name] ASC);
                END;
            END TRY
            BEGIN CATCH
                IF ERROR_NUMBER() NOT IN (2714, 1913, 2759) THROW;
            END CATCH;

            BEGIN TRY
                IF OBJECT_ID(N'{grantsObject}', N'U') IS NULL
                BEGIN
                    CREATE TABLE {grantsTable} (
                        [Id] uniqueidentifier NOT NULL,
                        [Name] nvarchar(128) NOT NULL,
                        [ProviderName] nvarchar(64) NOT NULL,
                        [ProviderKey] nvarchar(64) NOT NULL,
                        [TenantId] nvarchar(41) NULL,
                        [IsGranted] bit NOT NULL DEFAULT CAST(1 AS bit),
                        CONSTRAINT [PK_{options.PermissionGrantsTableName}] PRIMARY KEY CLUSTERED ([Id] ASC)
                    );
                    CREATE UNIQUE NONCLUSTERED INDEX [IX_{options.PermissionGrantsTableName}_TenantId_Name_ProviderName_ProviderKey] ON {grantsTable} ([TenantId] ASC, [Name] ASC, [ProviderName] ASC, [ProviderKey] ASC);
                END;
            END TRY
            BEGIN CATCH
                IF ERROR_NUMBER() NOT IN (2714, 1913, 2759) THROW;
            END CATCH;
            """;
    }
}
