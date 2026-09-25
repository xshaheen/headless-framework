// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting.Initialization;
using Headless.Sequences;
using Headless.Sequences.SqlServer;
using Headless.Testing.Tests;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Tests;

/// <summary>
/// The SQL Server table lifecycle: created once at startup, safe to re-run and to race, never created lazily, and
/// failing loudly when the database cannot be reached. Also the provider options' own validation.
/// </summary>
[Collection<SqlServerSequencesFixture>]
public sealed class SqlServerSequencesStorageInitializerTests(SqlServerSequencesFixture fixture) : TestBase
{
    [Fact]
    public async Task should_throw_and_keep_initializer_unmarked_when_database_unreachable()
    {
        // Port 1 accepts no connections, so the TCP connect fails before any authentication is attempted.
        const string unreachable =
            "Server=127.0.0.1,1;Database=missing;User Id=sa;Password=placeholder-never-used;Connect Timeout=2;TrustServerCertificate=true";
        using var host = _CreateHost(options => options.ConnectionString = unreachable);

        await FluentActions
            .Awaiting(() => host.StartAsync(AbortToken))
            .Should()
            .ThrowAsync<Exception>()
            .Where(e => e is SqlException || e.InnerException is SqlException);

        var initializer = host.Services.GetRequiredService<IEnumerable<IInitializer>>().Single();
        initializer.IsInitialized.Should().BeFalse();

        await FluentActions
            .Awaiting(() => initializer.WaitForInitializationAsync(AbortToken))
            .Should()
            .ThrowAsync<SqlException>();
    }

    [Fact]
    public async Task should_leave_one_table_and_one_primary_key_when_hosts_initialize_concurrently()
    {
        const string schema = "sequences_mssql_concurrent";
        await _DropSchemaAsync(schema);
        var hosts = Enumerable.Range(0, 5).Select(_ => _CreateHost(schema: schema)).ToArray();

        try
        {
            await Task.WhenAll(hosts.Select(h => h.StartAsync(AbortToken)));

            hosts
                .Select(h => h.Services.GetRequiredService<IEnumerable<IInitializer>>().Single().IsInitialized)
                .Should()
                .AllSatisfy(initialized => initialized.Should().BeTrue());
            (await _CountTablesAsync(schema)).Should().Be(1);
            (await _CountPrimaryKeysAsync(schema)).Should().Be(1);
        }
        finally
        {
            foreach (var host in hosts)
            {
                host.Dispose();
            }
        }
    }

    [Fact]
    public async Task should_leave_one_table_and_one_primary_key_when_the_initializer_runs_twice()
    {
        const string schema = "sequences_mssql_rerun";
        await _DropSchemaAsync(schema);
        using var host = _CreateHost(schema: schema);
        var initializer = host
            .Services.GetRequiredService<IEnumerable<IInitializer>>()
            .OfType<HostedInitializer>()
            .Single();

        await initializer.StartingAsync(AbortToken);
        await initializer.StartingAsync(AbortToken);

        initializer.IsInitialized.Should().BeTrue();
        (await _CountTablesAsync(schema)).Should().Be(1);
        (await _CountPrimaryKeysAsync(schema)).Should().Be(1);
    }

    [Fact]
    public async Task should_key_the_table_by_a_clustered_primary_key_over_tenant_name_and_partition()
    {
        const string schema = "sequences_mssql_shape";
        await _DropSchemaAsync(schema);
        using var host = _CreateHost(schema: schema);
        await host.StartAsync(AbortToken);

        // The increment's HOLDLOCK serializes first use through key-range locks on this exact index, so its shape and
        // the ordinal collation of the key columns are part of the contract, not an implementation detail.
        (
            await fixture.ScalarAsync(
                """
                SELECT count(*)
                FROM sys.indexes i
                JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
                JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
                WHERE i.object_id = OBJECT_ID(@table) AND i.is_primary_key = 1 AND i.type_desc = 'CLUSTERED'
                  AND c.collation_name = 'Latin1_General_100_BIN2'
                  AND (
                      (ic.key_ordinal = 1 AND c.name = 'tenant_id')
                      OR (ic.key_ordinal = 2 AND c.name = 'name')
                      OR (ic.key_ordinal = 3 AND c.name = 'partition')
                  )
                """,
                AbortToken,
                ("table", $"{schema}.{SqlServerSequencesOptions.DefaultTableName}")
            )
        )
            .Should()
            .Be(3);
        (
            await fixture.ScalarAsync(
                """
                SELECT count(*)
                FROM sys.index_columns ic
                JOIN sys.indexes i ON i.object_id = ic.object_id AND i.index_id = ic.index_id
                WHERE i.object_id = OBJECT_ID(@table) AND i.is_primary_key = 1
                """,
                AbortToken,
                ("table", $"{schema}.{SqlServerSequencesOptions.DefaultTableName}")
            )
        ).Should().Be(3);
        await host.StopAsync(AbortToken);
    }

    [Fact]
    public async Task should_number_in_a_configured_schema_and_table()
    {
        const string schema = "sequences_mssql_custom";
        const string table = "counters";
        await fixture.ExecuteAsync(SqlServerSequencesFixture.DropSchemaSql(schema, table), AbortToken);
        using var host = _CreateHost(options =>
        {
            options.ConnectionString = fixture.CountersConnectionString;
            options.Schema = schema;
            options.TableName = table;
        });
        await host.StartAsync(AbortToken);
        var generator = host.Services.GetRequiredService<ISequenceGenerator>();

        (await generator.NextAsync("custom", cancellationToken: AbortToken)).Should().Be(1);
        (await generator.NextAsync("custom", cancellationToken: AbortToken)).Should().Be(2);

        (await fixture.ReadValueAsync(new SequenceKey("", "custom", ""), schema, table, AbortToken)).Should().Be(2);
        await host.StopAsync(AbortToken);
    }

    [Fact]
    public async Task should_not_create_the_table_when_initialization_is_off()
    {
        const string schema = "sequences_mssql_no_init";
        await _DropSchemaAsync(schema);
        using var host = _CreateHost(options =>
        {
            options.ConnectionString = fixture.CountersConnectionString;
            options.Schema = schema;
            options.InitializeOnStartup = false;
        });

        await host.StartAsync(AbortToken);

        host.Services.GetRequiredService<IEnumerable<IInitializer>>().Single().IsInitialized.Should().BeTrue();
        (await _CountTablesAsync(schema)).Should().Be(0);

        // The consumer owns the table in this mode, so a call before it exists fails instead of creating it.
        var act = async () =>
            await host.Services.GetRequiredService<ISequenceGenerator>().NextAsync("x", cancellationToken: AbortToken);

        // 208: invalid object name.
        (await act.Should().ThrowAsync<SqlException>())
            .Which.Number.Should()
            .Be(208);
        (await _CountTablesAsync(schema)).Should().Be(0);
        await host.StopAsync(AbortToken);
    }

    [Fact]
    public void should_reject_an_empty_connection_string()
    {
        using var host = _CreateHost(options => options.ConnectionString = "");

        var act = () => host.Services.GetRequiredService<IOptions<SqlServerSequencesOptions>>().Value;

        act.Should().Throw<OptionsValidationException>().WithMessage("*ConnectionString*");
    }

    [Fact]
    public void should_reject_an_invalid_schema_identifier()
    {
        using var host = _CreateHost(options =>
        {
            options.ConnectionString = fixture.CountersConnectionString;
            options.Schema = "bad schema]; DROP TABLE x; --";
        });

        var act = () => host.Services.GetRequiredService<IOptions<SqlServerSequencesOptions>>().Value;

        act.Should().Throw<OptionsValidationException>().WithMessage("*Schema*");
    }

    [Fact]
    public void should_reject_an_invalid_table_identifier()
    {
        using var host = _CreateHost(options =>
        {
            options.ConnectionString = fixture.CountersConnectionString;
            options.TableName = "bad]table";
        });

        var act = () => host.Services.GetRequiredService<IOptions<SqlServerSequencesOptions>>().Value;

        act.Should().Throw<OptionsValidationException>().WithMessage("*TableName*");
    }

    [Fact]
    public void should_reject_a_blank_connection_string_at_the_call_site()
    {
        var services = new ServiceCollection();

        var act = () => services.AddHeadlessSequences(setup => setup.UseSqlServer("  "));

        act.Should().Throw<ArgumentException>();
    }

    private static IHost _CreateHost(Action<SqlServerSequencesOptions> configure)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddHeadlessSequences(setup => setup.UseSqlServer(configure));

        return builder.Build();
    }

    private IHost _CreateHost(string schema)
    {
        return _CreateHost(options =>
        {
            options.ConnectionString = fixture.CountersConnectionString;
            options.Schema = schema;
        });
    }

    private Task _DropSchemaAsync(string schema)
    {
        return fixture.ExecuteAsync(
            SqlServerSequencesFixture.DropSchemaSql(schema, SqlServerSequencesOptions.DefaultTableName),
            AbortToken
        );
    }

    private Task<int> _CountTablesAsync(string schema)
    {
        return fixture.ScalarAsync(
            "SELECT count(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = @schema AND TABLE_NAME = @table",
            AbortToken,
            ("schema", schema),
            ("table", SqlServerSequencesOptions.DefaultTableName)
        );
    }

    private Task<int> _CountPrimaryKeysAsync(string schema)
    {
        return fixture.ScalarAsync(
            """
            SELECT count(*)
            FROM sys.key_constraints k
            JOIN sys.tables t ON t.object_id = k.parent_object_id
            WHERE SCHEMA_NAME(t.schema_id) = @schema AND t.name = @table AND k.type = 'PK'
            """,
            AbortToken,
            ("schema", schema),
            ("table", SqlServerSequencesOptions.DefaultTableName)
        );
    }
}
