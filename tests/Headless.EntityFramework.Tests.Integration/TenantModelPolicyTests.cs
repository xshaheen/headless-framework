// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Domain;
using Headless.EntityFramework;
using Headless.MultiTenancy;
using Headless.Testing.Helpers;
using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class TenantModelPolicyTests : TestBase
{
    [Fact]
    public void should_resolve_after_base_declarations_and_use_model_property_names()
    {
        using var provider = _CreateProvider<DeclaredPolicy>();
        using var scope = provider.CreateScope();
        using var db = scope.ServiceProvider.GetRequiredService<PolicyContext<DeclaredPolicy>>();
        var entity = db.Model.FindEntityType(typeof(ExternalRow))!;

        entity.IsTenantOwned().Should().BeTrue();
        entity.GetTenantPropertyName().Should().Be("Owner");
        entity.FindProperty("Owner")!.IsNullable.Should().BeFalse();
        db.Set<ExternalRow>().ToQueryString().Should().Contain("tenant_column").And.Contain("Visible");
        db.Set<ExternalRow>()
            .IgnoreQueryFilters([HeadlessQueryFilters.MultiTenancyFilter])
            .ToQueryString()
            .Should()
            .Contain("Visible")
            .And.NotContain("WHERE FALSE");
    }

    [Fact]
    public void should_parameterize_tenant_from_each_context_sharing_the_model()
    {
        using var provider = _CreateProvider<DeclaredPolicy>();
        using var firstScope = provider.CreateScope();
        firstScope.ServiceProvider.GetRequiredService<TestCurrentTenant>().Id = "tenant-a";
        using var first = firstScope.ServiceProvider.GetRequiredService<PolicyContext<DeclaredPolicy>>();
        using var secondScope = provider.CreateScope();
        secondScope.ServiceProvider.GetRequiredService<TestCurrentTenant>().Id = "tenant-b";
        using var second = secondScope.ServiceProvider.GetRequiredService<PolicyContext<DeclaredPolicy>>();

        second.Model.Should().BeSameAs(first.Model);
        first.Set<ExternalRow>().ToQueryString().Should().Contain("tenant-a").And.NotContain("tenant-b");
        second.Set<ExternalRow>().ToQueryString().Should().Contain("tenant-b").And.NotContain("tenant-a");
    }

    [Fact]
    public void should_preserve_nullable_interface_host_policy_and_create_required_shadow_tenant()
    {
        using var provider = _CreateProvider<ShadowPolicy>();
        using var scope = provider.CreateScope();
        using var db = scope.ServiceProvider.GetRequiredService<PolicyContext<ShadowPolicy>>();

        var shadow = db.Model.FindEntityType(typeof(ExternalRow))!;
        shadow.FindProperty("TenantId")!.IsShadowProperty().Should().BeTrue();
        shadow.FindProperty("TenantId")!.IsNullable.Should().BeFalse();
        db.Model.FindEntityType(typeof(LegacyRow))!.FindProperty("TenantId")!.IsNullable.Should().BeTrue();
        db.Set<ExternalRow>().ToQueryString().Should().Contain("WHERE FALSE");
        db.Set<LegacyRow>().ToQueryString().Should().Contain("IS NULL");
    }

    [Fact]
    public void should_apply_canonical_column_policy_and_check_constraint()
    {
        using var provider = _CreateProvider<DeclaredPolicy>();
        using var scope = provider.CreateScope();
        using var db = scope.ServiceProvider.GetRequiredService<PolicyContext<DeclaredPolicy>>();
        var entity = db.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(ExternalRow))!;

        entity.FindProperty("Owner")!.GetCollation().Should().Be("C");
        entity.GetCheckConstraints().Should().ContainSingle(x => x.Sql == "right(\"tenant_column\", 1) <> ' '");
    }

    [Fact]
    public void should_reject_ambiguous_ambient_tenant_before_query_execution()
    {
        using var provider = _CreateProvider<DeclaredPolicy>();
        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<TestCurrentTenant>().Id = "tenant-a ";
        using var db = scope.ServiceProvider.GetRequiredService<PolicyContext<DeclaredPolicy>>();

        var query = () => db.Set<ExternalRow>().ToQueryString();
        query.Should().Throw<InvalidOperationException>().WithMessage("*U+0020*");
    }

    [Fact]
    public void should_allow_explicit_interface_root_opt_out()
    {
        using var provider = _CreateProvider<OptOutPolicy>();
        using var scope = provider.CreateScope();
        using var db = scope.ServiceProvider.GetRequiredService<PolicyContext<OptOutPolicy>>();
        var entity = db.Model.FindEntityType(typeof(LegacyRow))!;

        entity.IsTenantOwned().Should().BeFalse();
        entity.GetTenantPropertyName().Should().BeNull();
        db.Set<LegacyRow>().ToQueryString().Should().NotContain("WHERE");
    }

    [Fact]
    public void should_inherit_policy_for_owned_graphs_without_tenant_columns()
    {
        using var provider = _CreateProvider<OwnedPolicy>();
        using var scope = provider.CreateScope();
        using var db = scope.ServiceProvider.GetRequiredService<PolicyContext<OwnedPolicy>>();
        var root = db.Model.FindEntityType(typeof(ExternalRow))!;
        var owned = db.Model.GetEntityTypes().Single(x => x.IsOwned());

        owned.IsTenantOwned().Should().BeTrue();
        owned.GetTenantOwnerEntityType().Should().BeSameAs(root);
        owned.FindProperty("TenantId").Should().BeNull();
    }

    [Fact]
    public void should_reject_derived_conflicts() =>
        _AssertInvalid<ConflictingDerivedPolicy>("*conflicts with hierarchy root*");

    [Fact]
    public void should_reject_non_string_tenant_properties() => _AssertInvalid<WrongTypePolicy>("*must be a string*");

    [Fact]
    public void should_reject_keyless_tenant_roots() => _AssertInvalid<KeylessPolicy>("*requires a keyed*");

    [Fact]
    public void should_reject_shared_clr_tenant_roots() => _AssertInvalid<SharedPolicy>("*requires a keyed*");

    [Fact]
    public void should_reject_separately_stored_owned_graphs() =>
        _AssertInvalid<SeparateOwnedPolicy>("*separately stored owned entity*");

    [Fact]
    public void should_reject_incompatible_tenant_collations() =>
        _AssertInvalid<WrongCollationPolicy>("*requires collation 'C'*");

    [Fact]
    public void should_reject_fixed_length_column_types() =>
        _AssertInvalid<FixedLengthPolicy>("*variable-length Unicode storage*");

    [Fact]
    public void should_reject_tpt_inheritance() => _AssertInvalid<TptPolicy>("*requires a keyed*");

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Usage",
        "MA0045",
        Justification = "This helper asserts synchronous model-construction failures; its services perform no database I/O."
    )]
    private static void _AssertInvalid<TPolicy>(string message)
        where TPolicy : IPolicy
    {
        using var provider = _CreateProvider<TPolicy>();
        using var scope = provider.CreateScope();
        var build = () => scope.ServiceProvider.GetRequiredService<PolicyContext<TPolicy>>();
        build.Should().Throw<InvalidOperationException>().WithMessage(message);
    }

    private static ServiceProvider _CreateProvider<TPolicy>()
        where TPolicy : IPolicy
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<TestCurrentTenant>();
        services.AddScoped<ICurrentTenant>(provider => provider.GetRequiredService<TestCurrentTenant>());
        services.AddHeadlessDbContextServices();
        services.AddDbContext<PolicyContext<TPolicy>>(options =>
            options.UseNpgsql("Host=localhost;Database=tenant-model-only").AddHeadlessExtension()
        );
        return services.BuildServiceProvider();
    }

    private sealed class PolicyContext<TPolicy>(
        HeadlessDbContextServices services,
        DbContextOptions<PolicyContext<TPolicy>> options
    ) : HeadlessDbContext(services, options)
        where TPolicy : IPolicy
    {
        public override string? DefaultSchema => null;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            TPolicy.Configure(modelBuilder);
        }
    }

    private interface IPolicy
    {
        static abstract void Configure(ModelBuilder builder);
    }

    private sealed class DeclaredPolicy : IPolicy
    {
        public static void Configure(ModelBuilder builder)
        {
            builder.Entity<ExternalRow>().Ignore(x => x.Detail).IsTenantOwned("Owner");
            builder.Entity<ExternalRow>().Property(x => x.Owner).HasColumnName("tenant_column");
            builder.Entity<ExternalRow>().HasQueryFilter("Visible", x => x.Visible);
        }
    }

    private sealed class ShadowPolicy : IPolicy
    {
        public static void Configure(ModelBuilder builder)
        {
            builder.Entity(typeof(ExternalRow)).Ignore(nameof(ExternalRow.Detail)).IsTenantOwned();
            builder.Entity<LegacyRow>();
        }
    }

    private sealed class OptOutPolicy : IPolicy
    {
        public static void Configure(ModelBuilder builder) => builder.Entity<LegacyRow>().IsNotTenantOwned();
    }

    private sealed class OwnedPolicy : IPolicy
    {
        public static void Configure(ModelBuilder builder)
        {
            builder.Entity<ExternalRow>().IsTenantOwned();
            builder.Entity<ExternalRow>().OwnsOne(x => x.Detail);
        }
    }

    private sealed class SeparateOwnedPolicy : IPolicy
    {
        public static void Configure(ModelBuilder builder)
        {
            builder.Entity<ExternalRow>().IsTenantOwned();
            builder.Entity<ExternalRow>().OwnsOne(x => x.Detail, owned => owned.ToTable("Details"));
        }
    }

    private sealed class ConflictingDerivedPolicy : IPolicy
    {
        public static void Configure(ModelBuilder builder)
        {
            builder.Entity<LegacyRow>();
            builder.Entity<DerivedRow>().IsNotTenantOwned();
        }
    }

    private sealed class WrongTypePolicy : IPolicy
    {
        public static void Configure(ModelBuilder builder) => builder.Entity<LegacyRow>().IsTenantOwned("Id");
    }

    private sealed class KeylessPolicy : IPolicy
    {
        public static void Configure(ModelBuilder builder) => builder.Entity<LegacyRow>().HasNoKey();
    }

    private sealed class SharedPolicy : IPolicy
    {
        public static void Configure(ModelBuilder builder)
        {
            var shared = builder.SharedTypeEntity<Dictionary<string, object>>("Shared");
            shared.IndexerProperty<int>("Id");
            shared.HasKey("Id");
            shared.IsTenantOwned();
        }
    }

    private sealed class WrongCollationPolicy : IPolicy
    {
        public static void Configure(ModelBuilder builder) =>
            builder.Entity<LegacyRow>().Property(x => x.TenantId).UseCollation("en-US");
    }

    private sealed class FixedLengthPolicy : IPolicy
    {
        public static void Configure(ModelBuilder builder) =>
            builder.Entity<LegacyRow>().Property(x => x.TenantId).HasColumnType("character(16)");
    }

    private sealed class TptPolicy : IPolicy
    {
        public static void Configure(ModelBuilder builder)
        {
            builder.Entity<LegacyRow>().UseTptMappingStrategy();
            builder.Entity<DerivedRow>();
        }
    }

    private sealed class ExternalRow
    {
        public int Id { get; set; }
        public string? Owner { get; set; }
        public bool Visible { get; set; }
        public Detail? Detail { get; set; }
    }

    private sealed class Detail
    {
        public string? Text { get; set; }
    }

    private class LegacyRow : IMultiTenant
    {
        public int Id { get; set; }
        public string? TenantId { get; set; }
    }

    private sealed class DerivedRow : LegacyRow;
}
