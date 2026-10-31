// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.EntityFramework;
using Headless.Hosting;
using Headless.Search;
using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Tests;

/// <summary>
/// The function's lifecycle and its use from EF Core: created by <c>resources provision</c> as an immutable,
/// parallel-safe function, never re-created, created before the app's migrations, and served by a trigram index.
/// </summary>
[Collection<PostgreSqlSearchFixture>]
public sealed class ArabicSearchProvisioningTests(PostgreSqlSearchFixture fixture) : TestBase
{
    [Fact]
    public async Task should_create_an_immutable_strict_parallel_safe_function_once_when_provisioned_twice()
    {
        var connectionString = await fixture.CreateDatabaseAsync(AbortToken);
        using var host = PostgreSqlSearchFixture.CreateHost(connectionString);

        await host.Services.ProvisionResourcesAsync(AbortToken);
        await host.Services.ProvisionResourcesAsync(AbortToken);

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(AbortToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT p.provolatile::text, p.proparallel::text, p.proisstrict, count(*) OVER ()
            FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace
            WHERE n.nspname = 'headless' AND p.proname = 'ar_norm_v1'
            """,
            connection
        );
        await using var reader = await command.ExecuteReaderAsync(AbortToken);

        (await reader.ReadAsync(AbortToken)).Should().BeTrue("provision creates the function");
        reader.GetString(0).Should().Be("i", "the function is IMMUTABLE");
        reader.GetString(1).Should().Be("s", "the function is PARALLEL SAFE");
        reader.GetBoolean(2).Should().BeTrue("the function is STRICT");
        reader.GetInt64(3).Should().Be(1);
        await reader.DisposeAsync();

        await using var history = new NpgsqlCommand(
            "SELECT count(*) FROM headless.headless_schema_history WHERE feature = 'ArabicSearch'",
            connection
        );
        (await history.ExecuteScalarAsync(AbortToken)).Should().Be(1L);
    }

    [Fact]
    public async Task should_fail_the_start_until_the_function_is_provisioned()
    {
        var connectionString = await fixture.CreateDatabaseAsync(AbortToken);
        using var host = PostgreSqlSearchFixture.CreateHost(connectionString);

        var act = () => host.StartAsync(AbortToken);

        await act.Should().ThrowAsync<ResourceProvisioningException>().WithMessage("*ArabicSearch*");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task should_apply_a_migration_with_a_stored_key_after_creating_the_function_in_either_order(
        bool migrationsFirst
    )
    {
        var connectionString = await fixture.CreateDatabaseAsync(AbortToken);
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();

        if (migrationsFirst)
        {
            _AddPeopleContext(builder.Services, connectionString);
        }

        builder.Services.AddHeadlessSearch(setup => setup.UsePostgreSql(connectionString));

        if (!migrationsFirst)
        {
            _AddPeopleContext(builder.Services, connectionString);
        }

        using var host = builder.Build();

        await host.Services.ProvisionResourcesAsync(AbortToken);

        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PeopleDbContext>();
        db.People.AddRange(
            new Person { Name = "أحمد علي" },
            new Person { Name = "فاطمة الزهراء" },
            new Person { Name = "محمدي" },
            new Person { Name = "منذر" }
        );
        await db.SaveChangesAsync(AbortToken);

        (await _SearchAsync(db, "احمد")).Should().Equal("أحمد علي");
        (await _SearchAsync(db, "فاطمه")).Should().Equal("فاطمة الزهراء");
        (await _SearchAsync(db, "محمدى")).Should().Equal("محمدي");
        (await _SearchAsync(db, "مندر")).Should().BeEmpty("thal and dal stay distinct");
        (await db.People.Select(p => p.NameSearch).OrderBy(k => k).ToListAsync(AbortToken))
            .Should()
            .Equal("احمد علي", "فاطمه الزهراء", "محمدي", "منذر");
    }

    // The version upgrade path: SET EXPRESSION rewrites every stored key and keeps the trigram index usable.
    [Fact]
    public async Task should_rebuild_stored_keys_and_keep_the_index_when_set_expression_runs()
    {
        var connectionString = await fixture.CreateDatabaseAsync(AbortToken);
        using var host = PostgreSqlSearchFixture.CreateHost(
            connectionString,
            services => _AddPeopleContext(services, connectionString)
        );
        await host.Services.ProvisionResourcesAsync(AbortToken);
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(AbortToken);
        await _ExecuteAsync(connection, "INSERT INTO people (name) VALUES ('أحمد علي'), ('فاطمة')");
        var migration = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
        migration.SetArabicSearchKeyExpression("people", "name_search", "name", ArabicSearchVersion.V1);
        var sql = ((Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation)migration.Operations.Single()).Sql;

        await _ExecuteAsync(connection, sql);

        await using var keys = new NpgsqlCommand("SELECT name_search FROM people ORDER BY id", connection);
        var rebuilt = new List<string>();
        await using (var reader = await keys.ExecuteReaderAsync(AbortToken))
        {
            while (await reader.ReadAsync(AbortToken))
            {
                rebuilt.Add(reader.GetString(0));
            }
        }

        rebuilt.Should().Equal("احمد علي", "فاطمه");
        await using var index = new NpgsqlCommand(
            "SELECT indexdef FROM pg_indexes WHERE indexname = 'ix_people_name_search'",
            connection
        );
        ((string)(await index.ExecuteScalarAsync(AbortToken))!).Should().Contain("gin").And.Contain("gin_trgm_ops");
    }

    [Fact]
    public async Task should_serve_the_like_query_from_the_trigram_index()
    {
        var connectionString = await fixture.CreateDatabaseAsync(AbortToken);
        using var host = PostgreSqlSearchFixture.CreateHost(
            connectionString,
            services => _AddPeopleContext(services, connectionString)
        );
        await host.Services.ProvisionResourcesAsync(AbortToken);

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(AbortToken);
        await using (
            var seed = new NpgsqlCommand(
                "INSERT INTO people (name) SELECT 'اسم ' || g FROM generate_series(1, 2000) g; ANALYZE people;",
                connection
            )
        )
        {
            await seed.ExecuteNonQueryAsync(AbortToken);
        }

        await using (var noSeqScan = new NpgsqlCommand("SET enable_seqscan = off", connection))
        {
            await noSeqScan.ExecuteNonQueryAsync(AbortToken);
        }

        await using var explain = new NpgsqlCommand(
            // The predicate EF Core emits for EF.Functions.Like(key, "%" + ArabicSearchFunctions.Key(term) + "%").
            "EXPLAIN SELECT id FROM people WHERE name_search LIKE '%' || COALESCE(headless.ar_norm_v1($1), '') || '%' ESCAPE ''",
            connection
        );
        explain.Parameters.Add(new NpgsqlParameter { Value = "أحمد" });
        var plan = new List<string>();
        await using var reader = await explain.ExecuteReaderAsync(AbortToken);

        while (await reader.ReadAsync(AbortToken))
        {
            plan.Add(reader.GetString(0));
        }

        string.Join('\n', plan).Should().Contain("Bitmap Index Scan on ix_people_name_search");
    }

    [Fact]
    public async Task should_produce_trigrams_from_an_arabic_key_on_a_utf8_locale()
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);

        var trigrams = await _ShowTrigramsAsync(connection);

        // show_trgm prints a multibyte trigram as its hash, so count them: "محمد علي" has nine.
        trigrams.Should().HaveCount(9);
    }

    // pg_trgm decides what a word character is from the database's LC_CTYPE; under libc "C" no Arabic letter is one.
    // LIKE stays correct there (the index only rechecks every row), but similarity never matches and the index gives
    // no speed-up, so the docs require a UTF-8 aware locale.
    [Fact]
    public async Task should_produce_no_trigrams_from_an_arabic_key_on_a_c_ctype_database()
    {
        var connectionString = await fixture.CreateDatabaseAsync(AbortToken, lcCtype: "C");
        using var host = PostgreSqlSearchFixture.CreateHost(connectionString);
        await host.Services.ProvisionResourcesAsync(AbortToken);
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(AbortToken);

        var trigrams = await _ShowTrigramsAsync(connection);

        trigrams.Should().BeEmpty();
        await using var like = new NpgsqlCommand(
            "SELECT headless.ar_norm_v1('أحمد علي') LIKE '%' || headless.ar_norm_v1('احمد') || '%'",
            connection
        );
        (await like.ExecuteScalarAsync(AbortToken)).Should().Be(true);
    }

    private static async Task<string[]> _ShowTrigramsAsync(NpgsqlConnection connection)
    {
        await using (var extension = new NpgsqlCommand("CREATE EXTENSION IF NOT EXISTS pg_trgm", connection))
        {
            await extension.ExecuteNonQueryAsync(AbortToken);
        }

        await using var command = new NpgsqlCommand("SELECT show_trgm(headless.ar_norm_v1('محمد علي'))", connection);

        return (string[])(await command.ExecuteScalarAsync(AbortToken))!;
    }

    private static void _AddPeopleContext(IServiceCollection services, string connectionString)
    {
        services.AddDbContext<PeopleDbContext>(options => options.UseNpgsql(connectionString));
        services.AddDbContextMigrations<PeopleDbContext>();
    }

    private static async Task _ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(AbortToken);
    }

    private static Task<List<string>> _SearchAsync(PeopleDbContext db, string term)
    {
        return db
            .People.Where(p => EF.Functions.Like(p.NameSearch, "%" + ArabicSearchFunctions.Key(term) + "%"))
            .Select(p => p.Name)
            .ToListAsync(AbortToken);
    }
}

public sealed class Person
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? NameSearch { get; set; }
}

public sealed class PeopleDbContext(DbContextOptions<PeopleDbContext> options) : DbContext(options)
{
    public DbSet<Person> People => Set<Person>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasHeadlessArabicSearch(ArabicSearchVersion.V1);
        modelBuilder.Entity<Person>(entity =>
        {
            entity.ToTable("people");
            entity.Property(p => p.Id).HasColumnName("id");
            entity.Property(p => p.Name).HasColumnName("name");
            entity.Property(p => p.NameSearch).HasColumnName("name_search");
            entity.HasArabicSearchKey(p => p.Name, p => p.NameSearch);
            entity.HasIndex(p => p.NameSearch).HasDatabaseName("ix_people_name_search");
        });
    }
}

// Hand-written to match what EF Core scaffolds for PeopleDbContext, so the test runs no design-time tooling.
[DbContext(typeof(PeopleDbContext))]
[Migration("20261029000000_CreatePeople")]
public sealed class CreatePeople : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterDatabase().Annotation("Npgsql:PostgresExtension:pg_trgm", ",,");
        migrationBuilder.CreateTable(
            name: "people",
            columns: table => new
            {
                id = table
                    .Column<int>(nullable: false)
                    .Annotation(
                        "Npgsql:ValueGenerationStrategy",
                        Npgsql
                            .EntityFrameworkCore
                            .PostgreSQL
                            .Metadata
                            .NpgsqlValueGenerationStrategy
                            .IdentityByDefaultColumn
                    ),
                name = table.Column<string>(nullable: false),
                name_search = table.Column<string>(
                    nullable: true,
                    computedColumnSql: "\"headless\".ar_norm_v1(\"name\")",
                    stored: true
                ),
            },
            constraints: table => table.PrimaryKey("pk_people", x => x.id)
        );
        migrationBuilder
            .CreateIndex(name: "ix_people_name_search", table: "people", column: "name_search")
            .Annotation("Npgsql:IndexMethod", "gin")
            .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });
    }
}
