// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Testcontainers;
using Headless.Testing.Tests;
using Npgsql;

namespace Tests;

/// <summary>
/// A fixture on a private server whose database is cloned from a provisioned template. It runs privately because
/// these tests build and evict templates, which on the shared server would push out templates other runs rely on.
/// </summary>
[CollectionDefinition(DisableParallelization = true)]
public sealed class PostgreSqlTemplatedFixture
    : HeadlessPostgreSqlFixture,
        ICollectionFixture<PostgreSqlTemplatedFixture>
{
    public const string Key = "headless-testing-template-self-test";

    public int ProvisionCount { get; private set; }

    protected override bool ShareContainer => false;

    protected override string TemplateKey => Key;

    protected override async Task ProvisionTemplateAsync(string connectionString, CancellationToken cancellationToken)
    {
        ProvisionCount++;
        await PostgreSqlTemplateDatabaseTests.CreateSchemaAsync(connectionString, cancellationToken);

        // A pooled connection left open by the provisioning must not block the clone.
        await using var lingering = new NpgsqlConnection(connectionString);
        await lingering.OpenAsync(cancellationToken);
    }
}

[Collection<PostgreSqlTemplatedFixture>]
public sealed class PostgreSqlTemplateDatabaseTests(PostgreSqlTemplatedFixture fixture) : TestBase
{
    [Fact]
    public async Task should_clone_the_fixture_database_with_the_template_schema_and_seed_data()
    {
        // then
        fixture.ProvisionCount.Should().Be(1);
        fixture.DatabaseName.Should().MatchRegex("^hf_[0-9a-f]{12}$");
        (await _ScalarAsync(fixture.ConnectionString, "SELECT count(*) FROM app.widgets;")).Should().Be(3L);
        (await _ScalarAsync(fixture.ConnectionString, "SELECT name FROM app.widgets WHERE id = 2;"))
            .Should()
            .Be("second");
    }

    [Fact]
    public async Task should_build_a_template_once_when_fixtures_start_concurrently()
    {
        // given
        var key = "concurrent-" + Guid.NewGuid().ToString("N");
        var provisions = 0;

        async Task provision(string connectionString, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref provisions);
            await CreateSchemaAsync(connectionString, cancellationToken);
        }

        // when
        var owners = await Task.WhenAll(
            Enumerable
                .Range(0, 6)
                .Select(async _ =>
                {
                    var template = await PostgreSqlTemplateDatabase.EnsureAsync(
                        fixture.AdminConnectionString,
                        key,
                        provision,
                        AbortToken
                    );

                    return await PostgreSqlDatabaseOwnership.CreateAsync(
                        fixture.AdminConnectionString,
                        template,
                        AbortToken
                    );
                })
        );

        // then
        try
        {
            provisions.Should().Be(1);

            foreach (var owner in owners)
            {
                (await _ScalarAsync(fixture.ConnectionStringFor(owner.OwnerId), "SELECT count(*) FROM app.widgets;"))
                    .Should()
                    .Be(3L);
            }

            var template = PostgreSqlTemplateDatabase.NameFor(key);
            (await _ScalarAsync(fixture.AdminConnectionString, _TemplateStateSql(template)))
                .Should()
                .Be("template:true allow_connections:false");
        }
        finally
        {
            foreach (var owner in owners)
            {
                await owner.DisposeAsync();
            }
        }
    }

    [Fact]
    public async Task should_keep_templates_through_the_dead_owner_sweep()
    {
        // given
        var template = await PostgreSqlTemplateDatabase.EnsureAsync(
            fixture.AdminConnectionString,
            "sweep-" + Guid.NewGuid().ToString("N"),
            CreateSchemaAsync,
            AbortToken
        );
        var orphan = TestDatabaseOwner.NewId();
        await _ExecuteAsync(fixture.AdminConnectionString, $"CREATE DATABASE \"{orphan}\";");

        // when
        var next = await PostgreSqlDatabaseOwnership.CreateAsync(fixture.AdminConnectionString, AbortToken);
        await next.DisposeAsync();

        // then
        var databases = await _DatabasesAsync();
        databases.Should().NotContain(orphan);
        databases.Should().Contain(template, "a template has no owner, so the sweep never drops it");
        TestDatabaseOwner.OwnerOf(template).Should().BeNull();
    }

    [Fact]
    public async Task should_rebuild_a_template_whose_builder_died_before_marking_it_ready()
    {
        // given: a database with the template's name but no ready marker, as a killed build leaves it
        var key = "abandoned-" + Guid.NewGuid().ToString("N");
        var template = PostgreSqlTemplateDatabase.NameFor(key);
        await _ExecuteAsync(fixture.AdminConnectionString, $"CREATE DATABASE \"{template}\";");

        // when
        await PostgreSqlTemplateDatabase.EnsureAsync(fixture.AdminConnectionString, key, CreateSchemaAsync, AbortToken);

        // then
        (await _ScalarAsync(fixture.AdminConnectionString, _TemplateStateSql(template)))
            .Should()
            .Be("template:true allow_connections:false");
    }

    [Fact]
    public async Task should_not_mark_a_template_ready_when_provisioning_fails()
    {
        // given
        var key = "failing-" + Guid.NewGuid().ToString("N");

        // when
        var act = () =>
            PostgreSqlTemplateDatabase.EnsureAsync(
                fixture.AdminConnectionString,
                key,
                (_, _) => throw new InvalidOperationException("migration failed"),
                AbortToken
            );

        // then
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("migration failed");
        (await _DatabasesAsync()).Should().NotContain(PostgreSqlTemplateDatabase.NameFor(key));
    }

    [Fact]
    public async Task should_keep_at_most_the_newest_templates_when_a_new_one_is_built()
    {
        // given
        var keys = Enumerable
            .Range(0, PostgreSqlTemplateDatabase.MaxTemplates + 2)
            .Select(i => $"evict-{i}-{Guid.NewGuid():N}")
            .ToList();

        // when
        foreach (var key in keys)
        {
            await PostgreSqlTemplateDatabase.EnsureAsync(
                fixture.AdminConnectionString,
                key,
                (_, _) => Task.CompletedTask,
                AbortToken
            );
        }

        // then
        var templates = (await _DatabasesAsync())
            .Where(name => name.StartsWith(PostgreSqlTemplateDatabase.Prefix, StringComparison.Ordinal))
            .ToList();
        templates.Should().HaveCount(PostgreSqlTemplateDatabase.MaxTemplates);
        templates
            .Should()
            .Contain(keys.TakeLast(PostgreSqlTemplateDatabase.MaxTemplates).Select(PostgreSqlTemplateDatabase.NameFor));
    }

    [Fact]
    public void should_name_templates_apart_from_owner_databases_and_by_key()
    {
        var name = PostgreSqlTemplateDatabase.NameFor("a");

        name.Should().MatchRegex("^hftpl_[0-9a-f]{24}$");
        name.Should().NotStartWith(TestDatabaseOwner.Prefix);
        PostgreSqlTemplateDatabase.NameFor("a").Should().Be(name);
        PostgreSqlTemplateDatabase.NameFor("b").Should().NotBe(name);
    }

    internal static async Task CreateSchemaAsync(string connectionString, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            CREATE SCHEMA app;
            CREATE TABLE app.widgets (id integer PRIMARY KEY, name text NOT NULL);
            INSERT INTO app.widgets VALUES (1, 'first'), (2, 'second'), (3, 'third');
            """,
            connection
        );
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string _TemplateStateSql(string template)
    {
        return "SELECT 'template:' || datistemplate || ' allow_connections:' || datallowconn FROM pg_database "
            + $"WHERE datname = '{template}';";
    }

    private async Task<List<string>> _DatabasesAsync()
    {
        await using var connection = new NpgsqlConnection(fixture.AdminConnectionString);
        await connection.OpenAsync(AbortToken);
        await using var command = new NpgsqlCommand("SELECT datname FROM pg_database;", connection);
        await using var reader = await command.ExecuteReaderAsync(AbortToken);

        var names = new List<string>();

        while (await reader.ReadAsync(AbortToken))
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    private async Task<object?> _ScalarAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(AbortToken);
#pragma warning disable CA2100 // False positive: the statement names only a database this test generated.
        await using var command = new NpgsqlCommand(sql, connection);
#pragma warning restore CA2100
        return await command.ExecuteScalarAsync(AbortToken);
    }

    private async Task _ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(AbortToken);
#pragma warning disable CA2100 // False positive: the statement names only a database this test generated.
        await using var command = new NpgsqlCommand(sql, connection);
#pragma warning restore CA2100
        await command.ExecuteNonQueryAsync(AbortToken);
    }
}
