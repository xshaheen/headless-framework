// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.Hosting;
using Headless.Hosting.Schema;
using Headless.Permissions;
using Headless.Testing.Tests;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests;

[Collection<SqlServerPermissionsFixture>]
public sealed class SqlServerPermissionsColumnLengthTests(SqlServerPermissionsFixture fixture) : TestBase
{
    private const string _Schema = "permissions_sql_lengths";

    // The shape a consumer app composes from two prefixed identifiers: 72 characters, past the default 64.
    private static readonly string _LongProviderKey = "ws_" + new string('a', 32) + ":usr_" + new string('b', 32);

    [Fact]
    public async Task should_create_every_column_at_its_configured_length()
    {
        // given — a distinct length per option, so a column wired to the wrong option shows
        await _DropSchemaAsync();
        using var host = _CreateHost(options =>
        {
            options.PermissionGrantsNameMaxLength = 201;
            options.PermissionGrantsProviderNameMaxLength = 202;
            options.PermissionGrantsProviderKeyMaxLength = 203;
            options.PermissionGrantsTenantIdMaxLength = 204;
            options.PermissionDefinitionsNameMaxLength = 205;
            options.PermissionDefinitionsDisplayNameMaxLength = 206;
            options.PermissionDefinitionsProvidersMaxLength = 207;
            options.PermissionGroupDefinitionsNameMaxLength = 208;
            options.PermissionGroupDefinitionsDisplayNameMaxLength = 209;
        });

        // when
        await host.StartAsync(AbortToken);
        var lengths = await _ReadColumnLengthsAsync();

        // then
        lengths
            .Should()
            .BeEquivalentTo(
                new Dictionary<string, int>(StringComparer.Ordinal)
                {
                    ["permissiongrants.name"] = 201,
                    ["permissiongrants.providername"] = 202,
                    ["permissiongrants.providerkey"] = 203,
                    ["permissiongrants.tenantid"] = 204,
                    ["permissiondefinitions.name"] = 205,
                    ["permissiondefinitions.parentname"] = 205,
                    ["permissiondefinitions.displayname"] = 206,
                    ["permissiondefinitions.providers"] = 207,
                    ["permissiondefinitions.groupname"] = 208,
                    ["permissiongroupdefinitions.name"] = 208,
                    ["permissiongroupdefinitions.displayname"] = 209,
                }
            );
    }

    [Fact]
    public async Task should_grant_read_and_revoke_a_72_character_provider_key_when_its_column_is_widened_to_128()
    {
        // given
        await _DropSchemaAsync();
        using var host = _CreateHost(options => options.PermissionGrantsProviderKeyMaxLength = 128);
        await host.StartAsync(AbortToken);
        var repository = host.Services.GetRequiredService<IPermissionGrantRepository>();
        var grant = new PermissionGrantRecord(
            Guid.NewGuid(),
            "Users.Create",
            "User",
            _LongProviderKey,
            isGranted: true
        );

        // when
        await repository.InsertAsync(grant, AbortToken);
        var found = await repository.FindAsync("Users.Create", "User", _LongProviderKey, AbortToken);
        var listed = await repository.GetListAsync("User", _LongProviderKey, AbortToken);
        await repository.DeleteAsync(grant, AbortToken);
        var afterRevoke = await repository.FindAsync("Users.Create", "User", _LongProviderKey, AbortToken);

        // then
        _LongProviderKey.Should().HaveLength(72);
        found.Should().NotBeNull();
        found!.ProviderKey.Should().Be(_LongProviderKey);
        listed.Should().ContainSingle().Which.Id.Should().Be(grant.Id);
        afterRevoke.Should().BeNull();
    }

    [Fact]
    public async Task should_reject_a_72_character_provider_key_at_the_default_length()
    {
        // given — the default stays 64, so an application that configures nothing keeps the schema it had
        await _DropSchemaAsync();
        using var host = _CreateHost(_ => { });
        await host.StartAsync(AbortToken);
        var repository = host.Services.GetRequiredService<IPermissionGrantRepository>();
        var grant = new PermissionGrantRecord(
            Guid.NewGuid(),
            "Users.Create",
            "User",
            _LongProviderKey,
            isGranted: true
        );

        // when
        var act = () => repository.InsertAsync(grant, AbortToken);

        // then
        await act.Should().ThrowAsync<SqlException>();
    }

    [Fact]
    public async Task should_size_lookup_parameters_to_the_configured_columns()
    {
        // given — widths no other test configures, so the cached statement is this test's own
        await _DropSchemaAsync();
        using var host = _CreateHost(options =>
        {
            options.PermissionGrantsNameMaxLength = 133;
            options.PermissionGrantsProviderNameMaxLength = 67;
            options.PermissionGrantsProviderKeyMaxLength = 131;
        });
        await host.StartAsync(AbortToken);
        var repository = host.Services.GetRequiredService<IPermissionGrantRepository>();

        // when
        await repository.FindAsync("Users.Create", "Role", _LongProviderKey, AbortToken);

        // then — the statement SQL Server cached declares each lookup parameter at its column's width; a parameter
        // sized by the default constant would bind the longer key unsized, as nvarchar(max), and miss the plan
        var declarations = await _CachedParameterDeclarationsAsync();
        declarations
            .Should()
            .Contain(x =>
                x.Contains("@Name nvarchar(133)", StringComparison.Ordinal)
                && x.Contains("@ProviderName nvarchar(67)", StringComparison.Ordinal)
                && x.Contains("@ProviderKey nvarchar(131)", StringComparison.Ordinal)
            );
    }

    private async Task<List<string>> _CachedParameterDeclarationsAsync()
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);
        await using var command = new SqlCommand(
            """
            SELECT st.text
            FROM sys.dm_exec_query_stats qs
            CROSS APPLY sys.dm_exec_sql_text(qs.sql_handle) st
            WHERE st.text LIKE @pattern
            """,
            connection
        );
        command.Parameters.AddWithValue("@pattern", $"%[[]{_Schema}]%");
        await using var reader = await command.ExecuteReaderAsync(AbortToken);
        var statements = new List<string>();

        while (await reader.ReadAsync(AbortToken))
        {
            statements.Add(reader.GetString(0));
        }

        return statements;
    }

    private IHost _CreateHost(Action<PermissionsStorageOptions> configure)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton(TimeProvider.System);
        // Grant caching is required, and the host refuses to start without a registered cache.
        builder.Services.AddHeadlessCaching(setup => setup.UseInMemory());
        builder.Services.AddHeadlessPermissions(setup =>
        {
            setup.ConfigureStorage(options =>
            {
                options.Schema = _Schema;
                configure(options);
            });
            setup.UseSqlServer(fixture.ConnectionString);
        });

        builder.Services.AddDeployStepsOnStartup(o => o.Seed = false);

        return builder.Build();
    }

    private async Task _DropSchemaAsync()
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);
        await using var command = new SqlCommand(
            $"""
            DECLARE @sql nvarchar(max) = N'';
            SELECT @sql += N'DROP TABLE ' + QUOTENAME(s.name) + N'.' + QUOTENAME(t.name) + N';'
            FROM sys.tables t JOIN sys.schemas s ON s.schema_id = t.schema_id
            WHERE s.name = N'{_Schema}';
            IF EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'{_Schema}') SET @sql += N'DROP SCHEMA [{_Schema}];';
            EXEC sp_executesql @sql;
            """,
            connection
        );
        await command.ExecuteNonQueryAsync(AbortToken);
    }

    // Keys are lower-cased with underscores removed, so one expectation reads on either naming style.
    private async Task<Dictionary<string, int>> _ReadColumnLengthsAsync()
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);
        await using var command = new SqlCommand(
            $"""
            SELECT TABLE_NAME, COLUMN_NAME, CHARACTER_MAXIMUM_LENGTH
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = '{_Schema}' AND CHARACTER_MAXIMUM_LENGTH > 0
            """,
            connection
        );
        await using var reader = await command.ExecuteReaderAsync(AbortToken);
        var lengths = new Dictionary<string, int>(StringComparer.Ordinal);

        while (await reader.ReadAsync(AbortToken))
        {
            var table = reader.GetString(0);

            if (!table.StartsWith(SchemaRunner.HistoryTableName, StringComparison.Ordinal))
            {
                lengths[_Normalize(table) + "." + _Normalize(reader.GetString(1))] = reader.GetInt32(2);
            }
        }

        return lengths;
    }

    private static string _Normalize(string name)
    {
        return name.Replace("_", "", StringComparison.Ordinal).ToLowerInvariant();
    }
}
