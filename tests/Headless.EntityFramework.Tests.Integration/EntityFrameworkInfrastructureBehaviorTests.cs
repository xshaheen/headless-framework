// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Linq.Expressions;
using Headless.EntityFramework;
using Headless.EntityFramework.Seeders;
using Headless.Hosting.Seeders;
using Headless.MultiTenancy;
using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class EntityFrameworkInfrastructureBehaviorTests : TestBase
{
    [Fact]
    public void should_isolate_compiled_query_cache_keys_by_tenant_for_headless_contexts()
    {
        var expression = Expression.Constant(1);
        var otherExpression = Expression.Constant(2);
        var options = new DbContextOptionsBuilder<StubHeadlessDbContext>()
            .UseSqlite("Data Source=:memory:")
            .AddHeadlessExtension()
            .Options;
        using var context = new StubHeadlessDbContext(options, "tenant-a");
        var generator = context.GetService<ICompiledQueryCacheKeyGenerator>();

        var tenantA = generator.GenerateCacheKey(expression, async: true);
        var tenantARepeat = generator.GenerateCacheKey(expression, async: true);
        var tenantAOtherQuery = generator.GenerateCacheKey(otherExpression, async: true);
        context.TenantId = "tenant-b";
        var tenantB = generator.GenerateCacheKey(expression, async: true);

        tenantA.Should().Be(tenantARepeat);
        tenantA.Should().NotBe(tenantAOtherQuery);
        tenantA.Should().NotBe(tenantB);
        tenantA.GetHashCode().Should().Be(tenantARepeat.GetHashCode());
    }

    [Fact]
    public async Task should_report_invalid_configuration_when_recorded_tenant_guard_resolves_disabled()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.AddHeadlessTenancy(tenancy => tenancy.EntityFramework(ef => ef.GuardTenantWrites()));
        builder.Services.AddSingleton<IOptions<TenantWriteGuardOptions>>(
            Options.Create(new TenantWriteGuardOptions { IsEnabled = false })
        );
        await using var provider = builder.Services.BuildServiceProvider();
        var context = new HeadlessTenancyValidationContext(
            provider,
            provider.GetRequiredService<TenantPostureManifest>()
        );

        var diagnostics = provider
            .GetServices<IHeadlessTenancyValidator>()
            .SelectMany(validator => validator.Validate(context))
            .ToArray();

        diagnostics.Should().ContainSingle();
        diagnostics[0].Severity.Should().Be(HeadlessTenancyDiagnosticSeverity.Error);
        diagnostics[0].Code.Should().Be("HEADLESS_TENANCY_EF_WRITE_GUARD_DISABLED");
        diagnostics[0].Seam.Should().Be(HeadlessEntityFrameworkTenancyBuilder.Seam);
    }

    [Fact]
    public async Task should_emit_no_configuration_diagnostic_when_recorded_tenant_guard_is_enabled()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.AddHeadlessTenancy(tenancy => tenancy.EntityFramework(ef => ef.GuardTenantWrites()));
        await using var provider = builder.Services.BuildServiceProvider();
        var context = new HeadlessTenancyValidationContext(
            provider,
            provider.GetRequiredService<TenantPostureManifest>()
        );

        var diagnostics = provider
            .GetServices<IHeadlessTenancyValidator>()
            .SelectMany(validator => validator.Validate(context))
            .ToArray();

        diagnostics.Should().BeEmpty();
    }

    [Fact]
    public async Task should_fail_with_actionable_error_when_migration_seeder_has_no_context_registration()
    {
        await using var provider = new ServiceCollection().BuildServiceProvider();
        var seeder = new DbMigrationSeeder<UnregisteredDbContext>(provider);

        Func<Task> action = () => seeder.SeedAsync(AbortToken).AsTask();

        var exception = await action.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Contain(nameof(UnregisteredDbContext));
        exception.Which.Message.Should().Contain(nameof(IDbContextFactory<UnregisteredDbContext>));
    }

    [Fact]
    public void should_register_migration_seeder_once_when_registration_is_repeated()
    {
        var services = new ServiceCollection();

        services.AddDbMigrationSeeder<UnregisteredDbContext>();
        services.AddDbMigrationSeeder<UnregisteredDbContext>();

        using var provider = services.BuildServiceProvider();
        provider.GetServices<ISeeder>().Should().ContainSingle();
        provider.GetServices<DbMigrationSeeder<UnregisteredDbContext>>().Should().ContainSingle();
    }

    private sealed class StubHeadlessDbContext(DbContextOptions options, string? tenantId)
        : DbContext(options),
            IHeadlessDbContext
    {
        public string? TenantId { get; set; } = tenantId;

        public string? DefaultSchema => null;

        public IServiceProvider ServiceProvider => EmptyServiceProvider.Instance;
    }

    private sealed class UnregisteredDbContext(DbContextOptions<UnregisteredDbContext> options) : DbContext(options);

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public static EmptyServiceProvider Instance { get; } = new();

        public object? GetService(Type serviceType)
        {
            return null;
        }
    }
}
