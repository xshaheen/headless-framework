// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.AuditLog;
using Headless.Hosting;
using Headless.Hosting.Schema;
using Headless.Testing.Tests;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests;

/// <summary>
/// A database provisioned by a release whose correlation column held 128 characters: the upgrade widens it with one new
/// step that keeps the correlation index, and a host started before that step runs fails naming it.
/// </summary>
[Collection<SqlServerAuditLogFixture>]
public sealed class SqlServerAuditLogCorrelationUpgradeTests(SqlServerAuditLogFixture fixture) : TestBase
{
    private const string _Schema = "audit_log_sql_correlation_upgrade";
    private const string _Table = "AuditLogEntries";
    private const string _Index = "IX_AuditLogEntries_Correlation";
    private const string _WidenStep = "4";

    // The checksums the released steps recorded for this schema. A released step whose text moves fails every upgraded
    // database's start, so the upgrade must leave each one as it shipped.
    private static readonly string[] _ReleasedChecksums =
    [
        "1 6F3CDD98152348DE360AB8EEF2D135F3B34E0DA96B08DEA6A4EA45DB4AD43BAB",
        "2 4FBE30F25F8C5E6C0ECEF4BE0B5645B51BF7CA23E5DA2B08B050EF2F2A1E9BEB",
    ];

    [Fact]
    public async Task should_widen_the_correlation_column_with_only_the_new_step_and_keep_its_index_when_upgrading()
    {
        // given - a database as the earlier release left it: every earlier step recorded, the column at 128
        await _ExecuteAsync(
            $"""
            IF OBJECT_ID(N'{_Schema}.{_Table}', N'U') IS NOT NULL DROP TABLE [{_Schema}].[{_Table}];
            IF OBJECT_ID(N'{_Schema}.headless_schema_history', N'U') IS NOT NULL DROP TABLE [{_Schema}].[headless_schema_history];
            """
        );

        using (var provisioner = _CreateHost())
        {
            await provisioner.Services.ProvisionResourcesAsync(AbortToken);
        }

        var feature = _Feature();
        await _ExecuteAsync(
            $"""
            DELETE FROM [{_Schema}].[headless_schema_history] WHERE [Feature] = N'{feature}' AND [StepVersion] = N'{_WidenStep}';
            DROP INDEX [{_Index}] ON [{_Schema}].[{_Table}];
            ALTER TABLE [{_Schema}].[{_Table}] ALTER COLUMN [CorrelationId] nvarchar(128) NULL;
            CREATE NONCLUSTERED INDEX [{_Index}] ON [{_Schema}].[{_Table}] ([CorrelationId] ASC, [CreatedAt] ASC, [Id] ASC);
            """
        );

        (await _CorrelationCharactersAsync()).Should().Be(128);

        // when - the new build starts before the deployment ran resources provision
        using var early = _CreateHost();
        var start = () => early.StartAsync(AbortToken);

        // then
        (await start.Should().ThrowAsync<ResourceProvisioningException>()).WithMessage(
            $"*{_Schema}/{feature}*Steps {_WidenStep} of {feature} are not applied*resources provision*"
        );

        // when - the deployment provisions
        using var deploy = _CreateHost();
        var applied = await deploy.Services.GetRequiredService<SchemaRunner>().ApplyAsync(AbortToken);

        // then - only the new step ran, each released step kept the checksum it shipped with, the column holds 200
        // characters, and the correlation index keys on the same columns
        applied.AppliedSteps.Should().Equal(new SchemaAppliedStep(_Schema, feature, _WidenStep));
        (await _HistoryChecksumsAsync(feature)).Should().Contain(_ReleasedChecksums);
        (await _CorrelationCharactersAsync()).Should().Be(200);
        (await _IndexKeyColumnsAsync()).Should().Equal("CorrelationId", "CreatedAt", "Id");

        using var started = _CreateHost();
        await started.StartAsync(AbortToken);
        (await started.Services.GetRequiredService<SchemaRunner>().VerifyAsync(AbortToken)).Should().BeEmpty();
    }

    private string _Feature()
    {
        using var host = _CreateHost();

        return host.Services.GetRequiredService<SchemaRunner>().Contributions.Single().Feature;
    }

    private IHost _CreateHost()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddHeadlessAuditLog(setup =>
        {
            setup.ConfigureStorage(options => options.Schema = _Schema);
            setup.UseSqlServer(fixture.ConnectionString);
        });

        return builder.Build();
    }

    private async Task<int> _CorrelationCharactersAsync()
    {
        var bytes = await _ScalarAsync<short>(
            $"SELECT max_length FROM sys.columns WHERE object_id = OBJECT_ID(N'{_Schema}.{_Table}') AND name = N'CorrelationId';"
        );

        return bytes / 2;
    }

    private Task<List<string>> _IndexKeyColumnsAsync()
    {
        return _ListAsync(
            $"""
            SELECT c.name
            FROM sys.indexes i
            JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
            JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            WHERE i.object_id = OBJECT_ID(N'{_Schema}.{_Table}') AND i.name = N'{_Index}' AND ic.is_included_column = 0
            ORDER BY ic.key_ordinal;
            """
        );
    }

    private Task<List<string>> _HistoryChecksumsAsync(string feature)
    {
        return _ListAsync(
            $"""
            SELECT CONCAT([StepVersion] COLLATE DATABASE_DEFAULT, N' ', [Checksum] COLLATE DATABASE_DEFAULT) FROM [{_Schema}].[headless_schema_history]
            WHERE [Feature] = N'{feature}' ORDER BY [StepVersion];
            """
        );
    }

    private async Task _ExecuteAsync(string sql)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);
        await using var command = new SqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(AbortToken);
    }

    private async Task<T> _ScalarAsync<T>(string sql)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);
        await using var command = new SqlCommand(sql, connection);

        return (T)(await command.ExecuteScalarAsync(AbortToken))!;
    }

    private async Task<List<string>> _ListAsync(string sql)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(AbortToken);
        var values = new List<string>();

        while (await reader.ReadAsync(AbortToken))
        {
            values.Add(reader.GetString(0));
        }

        return values;
    }
}
