// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting.Initialization;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Headless.Features.SqlServer;

public sealed class SqlServerFeaturesStorageInitializer(
    IOptions<SqlServerFeaturesOptions> providerOptions,
    IOptions<FeaturesStorageOptions> storageOptions
) : IFeaturesStorageInitializer, IHostedService, IInitializer
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

    internal static string Qualified(FeaturesStorageOptions options, string tableName) =>
        $"[{options.Schema}].[{tableName}]";

    private static string _CreateScript(FeaturesStorageOptions options)
    {
        var valuesTable = Qualified(options, options.FeatureValuesTableName);
        var definitionsTable = Qualified(options, options.FeatureDefinitionsTableName);
        var groupsTable = Qualified(options, options.FeatureGroupDefinitionsTableName);
        var valuesObject = $"{options.Schema}.{options.FeatureValuesTableName}";
        var definitionsObject = $"{options.Schema}.{options.FeatureDefinitionsTableName}";
        var groupsObject = $"{options.Schema}.{options.FeatureGroupDefinitionsTableName}";

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
                        CONSTRAINT [PK_{options.FeatureGroupDefinitionsTableName}] PRIMARY KEY CLUSTERED ([Id] ASC)
                    );
                    CREATE UNIQUE NONCLUSTERED INDEX [IX_{options.FeatureGroupDefinitionsTableName}_Name] ON {groupsTable} ([Name] ASC);
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
                        [ParentName] nvarchar(128) NULL,
                        [Description] nvarchar(256) NULL,
                        [DefaultValue] nvarchar(256) NULL,
                        [IsVisibleToClients] bit NOT NULL,
                        [IsAvailableToHost] bit NOT NULL,
                        [Providers] nvarchar(256) NULL,
                        [ExtraProperties] nvarchar(max) NOT NULL,
                        CONSTRAINT [PK_{options.FeatureDefinitionsTableName}] PRIMARY KEY CLUSTERED ([Id] ASC)
                    );
                    CREATE NONCLUSTERED INDEX [IX_{options.FeatureDefinitionsTableName}_GroupName] ON {definitionsTable} ([GroupName] ASC);
                    CREATE UNIQUE NONCLUSTERED INDEX [IX_{options.FeatureDefinitionsTableName}_Name] ON {definitionsTable} ([Name] ASC);
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
                        [Value] nvarchar(128) NOT NULL,
                        [ProviderName] nvarchar(64) NOT NULL,
                        [ProviderKey] nvarchar(64) NULL,
                        CONSTRAINT [PK_{options.FeatureValuesTableName}] PRIMARY KEY CLUSTERED ([Id] ASC)
                    );
                    CREATE NONCLUSTERED INDEX [IX_{options.FeatureValuesTableName}_ProviderName_ProviderKey] ON {valuesTable} ([ProviderName] ASC, [ProviderKey] ASC);
                    CREATE UNIQUE NONCLUSTERED INDEX [IX_{options.FeatureValuesTableName}_Name_ProviderName_ProviderKey] ON {valuesTable} ([Name] ASC, [ProviderName] ASC, [ProviderKey] ASC);
                END;
            END TRY
            BEGIN CATCH
                IF ERROR_NUMBER() NOT IN (2714, 1913, 2759) THROW;
            END CATCH;
            """;
    }
}
