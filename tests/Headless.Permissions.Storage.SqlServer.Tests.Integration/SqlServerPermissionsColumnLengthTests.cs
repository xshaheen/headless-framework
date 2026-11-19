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
    public async Task should_size_lookup_parameters_to_the_configured_columns()
    {
        // given — widths no other test configures, so the cached statement is this test's own
        await fixture.DropSchemaAsync(_Schema, AbortToken);
        using var host = fixture.CreateHost(
            _Schema,
            options =>
            {
                options.PermissionGrantsNameMaxLength = 133;
                options.PermissionGrantsProviderNameMaxLength = 67;
                options.PermissionGrantsProviderKeyMaxLength = 131;
            }
        );
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
}
