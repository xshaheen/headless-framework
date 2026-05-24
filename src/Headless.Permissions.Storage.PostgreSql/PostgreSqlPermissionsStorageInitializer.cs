// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting.Initialization;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Headless.Permissions.PostgreSql;

public sealed class PostgreSqlPermissionsStorageInitializer(
    IOptions<PostgreSqlPermissionsOptions> providerOptions,
    IOptions<PermissionsStorageOptions> storageOptions
) : IPermissionsStorageInitializer, IHostedService, IInitializer
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
        var options = storageOptions.Value;
        var sql = _CreateScript(options);
        await using var connection = providerOptions.Value.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (PostgresException ex) when (ex.SqlState is "42P05" or "42P06" or "42P07")
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static string _CreateScript(PermissionsStorageOptions options)
    {
        var grantsTable = _Qualified(options.Schema, options.PermissionGrantsTableName);
        var definitionsTable = _Qualified(options.Schema, options.PermissionDefinitionsTableName);
        var groupsTable = _Qualified(options.Schema, options.PermissionGroupDefinitionsTableName);

        return $"""
            CREATE SCHEMA IF NOT EXISTS "{options.Schema}";

            CREATE TABLE IF NOT EXISTS {groupsTable} (
                "Id" uuid NOT NULL,
                "Name" character varying(128) NOT NULL,
                "DisplayName" character varying(256) NOT NULL,
                "ExtraProperties" text NOT NULL,
                CONSTRAINT "PK_{options.PermissionGroupDefinitionsTableName}" PRIMARY KEY ("Id")
            );

            CREATE TABLE IF NOT EXISTS {definitionsTable} (
                "Id" uuid NOT NULL,
                "GroupName" character varying(128) NOT NULL,
                "Name" character varying(128) NOT NULL,
                "DisplayName" character varying(256) NOT NULL,
                "IsEnabled" boolean NOT NULL,
                "ParentName" character varying(128),
                "Providers" character varying(128),
                "ExtraProperties" text NOT NULL,
                CONSTRAINT "PK_{options.PermissionDefinitionsTableName}" PRIMARY KEY ("Id")
            );

            CREATE TABLE IF NOT EXISTS {grantsTable} (
                "Id" uuid NOT NULL,
                "Name" character varying(128) NOT NULL,
                "ProviderName" character varying(64) NOT NULL,
                "ProviderKey" character varying(64) NOT NULL,
                "TenantId" character varying(41),
                "IsGranted" boolean NOT NULL DEFAULT TRUE,
                CONSTRAINT "PK_{options.PermissionGrantsTableName}" PRIMARY KEY ("Id")
            );

            CREATE UNIQUE INDEX IF NOT EXISTS "IX_{options.PermissionGroupDefinitionsTableName}_Name" ON {groupsTable} ("Name");
            CREATE INDEX IF NOT EXISTS "IX_{options.PermissionDefinitionsTableName}_GroupName" ON {definitionsTable} ("GroupName");
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_{options.PermissionDefinitionsTableName}_Name" ON {definitionsTable} ("Name");
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_{options.PermissionGrantsTableName}_TenantId_Name_ProviderName_ProviderKey" ON {grantsTable} ("TenantId", "Name", "ProviderName", "ProviderKey");
            """;
    }

    internal static string Qualified(PermissionsStorageOptions options, string tableName) => _Qualified(options.Schema, tableName);

    private static string _Qualified(string schema, string tableName) => $@"""{schema}"".""{tableName}""";
}
