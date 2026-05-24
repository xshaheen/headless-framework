// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting.Initialization;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Headless.Settings.SqlServer;

public sealed class SqlServerSettingsStorageInitializer(
    IOptions<SqlServerSettingsOptions> providerOptions,
    IOptions<SettingsStorageOptions> storageOptions
) : ISettingsStorageInitializer, IHostedService, IInitializer
{
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public bool IsInitialized { get; private set; }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
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

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task WaitForInitializationAsync(CancellationToken cancellationToken = default)
    {
        await _completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(providerOptions.Value.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new SqlCommand(_CreateScript(storageOptions.Value), connection);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    internal static string Qualified(SettingsStorageOptions options, string tableName) =>
        $"[{options.Schema}].[{tableName}]";

    private static string _CreateScript(SettingsStorageOptions options)
    {
        var valuesTable = Qualified(options, options.SettingValuesTableName);
        var definitionsTable = Qualified(options, options.SettingDefinitionsTableName);
        var valuesObject = $"{options.Schema}.{options.SettingValuesTableName}";
        var definitionsObject = $"{options.Schema}.{options.SettingDefinitionsTableName}";

        return $"""
            BEGIN TRY
                IF NOT EXISTS (SELECT * FROM sys.schemas WHERE name = N'{options.Schema}')
                    EXEC(N'CREATE SCHEMA [{options.Schema}]');
            END TRY
            BEGIN CATCH
                IF ERROR_NUMBER() NOT IN (2714, 1913, 2759) THROW;
            END CATCH;

            BEGIN TRY
                IF OBJECT_ID(N'{definitionsObject}', N'U') IS NULL
                BEGIN
                    CREATE TABLE {definitionsTable} (
                        [Id] uniqueidentifier NOT NULL,
                        [Name] nvarchar(128) NOT NULL,
                        [DisplayName] nvarchar(256) NOT NULL,
                        [Description] nvarchar(512) NULL,
                        [DefaultValue] nvarchar(2000) NULL,
                        [IsVisibleToClients] bit NOT NULL,
                        [IsInherited] bit NOT NULL,
                        [IsEncrypted] bit NOT NULL,
                        [Providers] nvarchar(1024) NULL,
                        [ExtraProperties] nvarchar(max) NOT NULL,
                        CONSTRAINT [PK_{options.SettingDefinitionsTableName}] PRIMARY KEY CLUSTERED ([Id] ASC)
                    );
                    CREATE UNIQUE NONCLUSTERED INDEX [IX_{options.SettingDefinitionsTableName}_Name] ON {definitionsTable} ([Name] ASC);
                END;
            END TRY
            BEGIN CATCH
                IF ERROR_NUMBER() NOT IN (2714, 1913, 2759) THROW;
            END CATCH;

            BEGIN TRY
                IF OBJECT_ID(N'{valuesObject}', N'U') IS NULL
                BEGIN
                    CREATE TABLE {valuesTable} (
                        [Id] uniqueidentifier NOT NULL,
                        [Name] nvarchar(128) NOT NULL,
                        [Value] nvarchar(2000) NOT NULL,
                        [ProviderName] nvarchar(64) NOT NULL,
                        [ProviderKey] nvarchar(64) NULL,
                        [DateCreated] datetimeoffset NOT NULL,
                        [DateUpdated] datetimeoffset NULL,
                        CONSTRAINT [PK_{options.SettingValuesTableName}] PRIMARY KEY CLUSTERED ([Id] ASC)
                    );
                    CREATE UNIQUE NONCLUSTERED INDEX [IX_{options.SettingValuesTableName}_Name_ProviderName_ProviderKey] ON {valuesTable} ([Name] ASC, [ProviderName] ASC, [ProviderKey] ASC);
                END;
            END TRY
            BEGIN CATCH
                IF ERROR_NUMBER() NOT IN (2714, 1913, 2759) THROW;
            END CATCH;
            """;
    }
}
