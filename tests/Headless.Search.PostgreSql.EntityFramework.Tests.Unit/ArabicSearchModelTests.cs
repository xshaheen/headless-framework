// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Search;
using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Tests;

public sealed class ArabicSearchModelTests : TestBase
{
    [Fact]
    public void should_store_a_key_computed_by_the_model_function_with_a_gin_trigram_index()
    {
        using var context = new SearchDbContext(
            static (model, entity) =>
            {
                model.HasHeadlessArabicSearch(ArabicSearchVersion.V1);
                entity.HasArabicSearchKey(p => p.Name, p => p.NameSearch);
            }
        );

        var person = _DesignModel(context).FindEntityType(typeof(Person))!;
        var key = person.FindProperty(nameof(Person.NameSearch))!;

        key.GetComputedColumnSql().Should().Be("\"headless\".ar_norm_v1(\"Name\")");
        key.GetIsStored().Should().BeTrue();
        var index = person.GetIndexes().Should().ContainSingle().Which;
        index.Properties.Should().ContainSingle().Which.Should().BeSameAs(key);
        index.GetMethod().Should().Be("gin");
        index.GetOperators().Should().Equal("gin_trgm_ops");
        _DesignModel(context).GetPostgresExtensions().Should().Contain(e => e.Name == "pg_trgm");
    }

    [Fact]
    public void should_use_the_configured_schema_and_an_explicit_source_column()
    {
        using var context = new SearchDbContext(
            static (model, entity) =>
            {
                model.HasHeadlessArabicSearch(ArabicSearchVersion.V1, schema: "search");
                entity.HasArabicSearchKey(p => p.Name, "name_key", sourceColumn: "full_name");
            }
        );

        var key = _DesignModel(context).FindEntityType(typeof(Person))!.FindProperty("name_key")!;

        key.IsShadowProperty().Should().BeTrue();
        key.GetComputedColumnSql().Should().Be("\"search\".ar_norm_v1(\"full_name\")");
    }

    [Fact]
    public void should_read_the_source_column_name_configured_before_the_call()
    {
        using var context = new SearchDbContext(
            static (model, entity) =>
            {
                model.HasHeadlessArabicSearch(ArabicSearchVersion.V1);
                entity.Property(p => p.Name).HasColumnName("display_name");
                entity.HasArabicSearchKey(p => p.Name, p => p.NameSearch);
            }
        );

        var key = _DesignModel(context).FindEntityType(typeof(Person))!.FindProperty(nameof(Person.NameSearch))!;

        key.GetComputedColumnSql().Should().Be("\"headless\".ar_norm_v1(\"display_name\")");
    }

    [Fact]
    public void should_throw_naming_the_missing_call_when_the_model_has_no_search_version()
    {
        using var context = new SearchDbContext(
            static (_, entity) => entity.HasArabicSearchKey(p => p.Name, p => p.NameSearch)
        );

        var act = () => context.Model;

        act.Should().Throw<InvalidOperationException>().WithMessage("*Person*HasHeadlessArabicSearch*");
    }

    [Fact]
    public void should_translate_the_query_function_to_the_mapped_database_function()
    {
        using var context = new SearchDbContext(
            static (model, entity) =>
            {
                model.HasHeadlessArabicSearch(ArabicSearchVersion.V1);
                entity.HasArabicSearchKey(p => p.Name, p => p.NameSearch);
            }
        );
#pragma warning disable RCS1118 // Fix makes the code worse: a const is inlined as a SQL literal, and the test needs the term sent as a parameter.
        var term = "احمد";
#pragma warning restore RCS1118

        var sql = context
            .People.Where(p => EF.Functions.Like(p.NameSearch, "%" + ArabicSearchFunctions.Key(term) + "%"))
            .ToQueryString();

        sql.Should().Contain("LIKE '%' || COALESCE(headless.ar_norm_v1(@term), '') || '%'");
    }

    [Fact]
    public void should_throw_when_the_query_function_is_called_outside_a_query()
    {
        var act = () => ArabicSearchFunctions.Key("احمد");

        act.Should().Throw<InvalidOperationException>().WithMessage("*EF Core query*ArabicSearchNormalizer*");
    }

    [Fact]
    public void should_rebuild_a_key_with_set_expression_when_moving_versions()
    {
        var migration = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");

        migration.SetArabicSearchKeyExpression("people", "name_search", "name", ArabicSearchVersion.V1, schema: "app");

        migration
            .Operations.Should()
            .ContainSingle()
            .Which.Should()
            .BeOfType<SqlOperation>()
            .Which.Sql.Should()
            .Be(
                "ALTER TABLE \"app\".\"people\" ALTER COLUMN \"name_search\" SET EXPRESSION AS "
                    + "(\"headless\".ar_norm_v1(\"name\"));"
            );
    }

    private static IModel _DesignModel(DbContext context) => context.GetService<IDesignTimeModel>().Model;

    private sealed class SearchDbContext(
        Action<ModelBuilder, Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<Person>> configure
    ) : DbContext
    {
        public DbSet<Person> People => Set<Person>();

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
            optionsBuilder
                .UseNpgsql("Host=localhost;Database=search;Username=postgres;Password=unused")
                // Each test builds a different model for the same context type.
                .ReplaceService<IModelCacheKeyFactory, PerInstanceModelCacheKeyFactory>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            configure(modelBuilder, modelBuilder.Entity<Person>());
        }
    }

    private sealed class PerInstanceModelCacheKeyFactory : IModelCacheKeyFactory
    {
        public object Create(DbContext context, bool designTime) => (context, designTime);
    }

    private sealed class Person
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public string? NameSearch { get; set; }
    }
}
