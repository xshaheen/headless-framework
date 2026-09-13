// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.EntityFramework;
using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

namespace Tests;

public sealed class TenantIndexPolicyTests : TestBase
{
    [Fact]
    public void should_append_tenant_to_selected_unique_index_after_configuration()
    {
        using var provider = _CreateProvider<SelectedIndex>();
        using var scope = provider.CreateScope();
        using var db = scope.ServiceProvider.GetRequiredService<IndexContext<SelectedIndex>>();
        var entity = db.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(Row))!;
        var index = entity.GetIndexes().Single(x => x.Name == "BusinessKey");

        index.Properties.Select(x => x.Name).Should().Equal("Code", "Region", "TenantId");
        index.GetDatabaseName().Should().Be("UX_business");
        index.IsDescending.Should().Equal(true, true, false);
        index.GetFilter().Should().Be("\"Code\" IS NOT NULL");
        index.IsUnique.Should().BeTrue();
        index.GetOperators().Should().Equal("text_ops");
        index.GetCollation().Should().Equal("C");
        index.GetNullSortOrder().Should().Equal(NullSortOrder.NullsLast);
        index["Application:Purpose"].Should().Be("lookup");
        entity.FindPrimaryKey()!.Properties.Select(x => x.Name).Should().Equal("Id");
        entity.GetIndexes().Single(x => x.Name == "Unselected").Properties.Select(x => x.Name).Should().Equal("Region");
    }

    [Fact]
    public void should_preserve_explicit_directions_and_avoid_duplicate_tenant_columns()
    {
        using var provider = _CreateProvider<ExplicitDirections>();
        using var scope = provider.CreateScope();
        using var db = scope.ServiceProvider.GetRequiredService<IndexContext<ExplicitDirections>>();
        var indexes = db.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(Row))!.GetIndexes().ToArray();
        indexes.Single(x => x.Name == "Mixed").IsDescending.Should().Equal(false, true, false);
        indexes
            .Single(x => x.Name == "AlreadyScoped")
            .Properties.Select(x => x.Name)
            .Should()
            .Equal("TenantId", "Code");
    }

    [Fact]
    public void should_preserve_computed_database_name_and_ascending_default()
    {
        using var provider = _CreateProvider<UnnamedIndex>();
        using var scope = provider.CreateScope();
        using var db = scope.ServiceProvider.GetRequiredService<IndexContext<UnnamedIndex>>();
        var index = db.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(Row))!.GetIndexes().Single();
        index.Name.Should().BeNull();
        index.GetDatabaseName().Should().Be("IX_Row_Code");
        index.IsDescending.Should().BeNull();
    }

    [Fact]
    public void should_reject_nonunique_selection() => _AssertInvalid<NonuniqueIndex>("*must be unique*");

    [Fact]
    public void should_reject_unknown_positional_annotation() =>
        _AssertInvalid<UnknownAnnotation>("*cannot preserve annotation 'Application:Columns'*");

    [Fact]
    public void should_reject_oversized_positional_annotation() =>
        _AssertInvalid<OversizedOperators>("*cannot preserve annotation 'Npgsql:IndexOperators'*");

    [Fact]
    public void should_reject_tenant_already_in_include_list() =>
        _AssertInvalid<TenantIncluded>("*cannot preserve annotation 'Npgsql:IndexInclude'*");

    [Fact]
    public void should_reject_fulltext_expression_indexes() =>
        _AssertInvalid<FulltextIndex>("*cannot preserve annotation 'Npgsql:TsVectorConfig'*");

    private static void _AssertInvalid<TPolicy>(string message)
        where TPolicy : IPolicy
    {
        using var provider = _CreateProvider<TPolicy>();
        using var scope = provider.CreateScope();
        var build = () => scope.ServiceProvider.GetRequiredService<IndexContext<TPolicy>>();
        build.Should().Throw<InvalidOperationException>().WithMessage(message);
    }

    private static ServiceProvider _CreateProvider<TPolicy>()
        where TPolicy : IPolicy
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessDbContextServices();
        services.AddDbContext<IndexContext<TPolicy>>(options =>
            options.UseNpgsql("Host=localhost;Database=index-model-only").AddHeadlessExtension()
        );
        return services.BuildServiceProvider();
    }

    private sealed class IndexContext<TPolicy>(
        HeadlessDbContextServices services,
        DbContextOptions<IndexContext<TPolicy>> options
    ) : HeadlessDbContext(services, options)
        where TPolicy : IPolicy
    {
        public override string? DefaultSchema => null;

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);
            builder.Entity<Row>().IsTenantOwned();
            TPolicy.Configure(builder);
        }
    }

    private interface IPolicy
    {
        static abstract void Configure(ModelBuilder builder);
    }

    private sealed class SelectedIndex : IPolicy
    {
        public static void Configure(ModelBuilder builder)
        {
            var index = builder.Entity<Row>().HasIndex(x => new { x.Code, x.Region }, "BusinessKey");
            index.IsTenantScoped().Should().BeSameAs(index);
            ((Microsoft.EntityFrameworkCore.Metadata.Builders.IndexBuilder)index)
                .IsTenantScoped()
                .Should()
                .BeSameAs(index);
            index
                .IsUnique()
                .IsDescending()
                .HasDatabaseName("UX_business")
                .HasFilter("\"Code\" IS NOT NULL")
                .HasOperators("text_ops")
                .UseCollation("C")
                .HasNullSortOrder(NullSortOrder.NullsLast)
                .HasAnnotation("Application:Purpose", "lookup");
            builder.Entity<Row>().HasIndex(x => x.Region, "Unselected").IsUnique();
        }
    }

    private sealed class ExplicitDirections : IPolicy
    {
        public static void Configure(ModelBuilder builder)
        {
            builder
                .Entity<Row>()
                .HasIndex(x => new { x.Code, x.Region }, "Mixed")
                .IsUnique()
                .IsDescending(false, true)
                .IsTenantScoped();
            builder.Entity<Row>().Property<string>("TenantId");
            builder
                .Entity<Row>()
                .HasIndex(["TenantId", "Code"], "AlreadyScoped")
                .IsUnique()
                .IsTenantScoped()
                .IsTenantScoped();
        }
    }

    private sealed class UnnamedIndex : IPolicy
    {
        public static void Configure(ModelBuilder builder) =>
            builder.Entity<Row>().HasIndex(x => x.Code).IsUnique().IsTenantScoped();
    }

    private sealed class NonuniqueIndex : IPolicy
    {
        public static void Configure(ModelBuilder builder) =>
            builder.Entity<Row>().HasIndex(x => x.Code).IsTenantScoped();
    }

    private sealed class UnknownAnnotation : IPolicy
    {
        public static void Configure(ModelBuilder builder) =>
            builder
                .Entity<Row>()
                .HasIndex(x => x.Code)
                .IsUnique()
                .IsTenantScoped()
                .HasAnnotation("Application:Columns", new[] { "Code" });
    }

    private sealed class OversizedOperators : IPolicy
    {
        public static void Configure(ModelBuilder builder) =>
            builder
                .Entity<Row>()
                .HasIndex(x => x.Code)
                .IsUnique()
                .IsTenantScoped()
                .HasOperators("text_ops", "text_ops");
    }

    private sealed class FulltextIndex : IPolicy
    {
        public static void Configure(ModelBuilder builder) =>
            builder
                .Entity<Row>()
                .HasIndex(x => x.Code)
                .IsUnique()
                .IsTenantScoped()
                .IsTsVectorExpressionIndex("english");
    }

    private sealed class TenantIncluded : IPolicy
    {
        public static void Configure(ModelBuilder builder)
        {
            builder.Entity<Row>().Property<string>("TenantId");
            NpgsqlIndexBuilderExtensions.IncludeProperties(
                builder.Entity<Row>().HasIndex(x => x.Code).IsUnique().IsTenantScoped(),
                "TenantId"
            );
        }
    }

    private sealed class Row
    {
        public int Id { get; set; }
        public string? Code { get; set; }
        public string? Region { get; set; }
    }
}
