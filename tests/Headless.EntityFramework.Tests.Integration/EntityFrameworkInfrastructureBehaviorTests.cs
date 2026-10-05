// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.EntityFramework;
using Headless.Hosting;
using Headless.MultiTenancy;
using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class EntityFrameworkInfrastructureBehaviorTests : TestBase
{
    [Fact]
    public async Task should_report_invalid_configuration_when_recorded_tenant_guard_resolves_disabled()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.AddHeadlessTenancy(tenancy => tenancy.EntityFramework(ef => ef.GuardTenantWrites()));
        builder.Services.AddSingleton(Options.Create(new TenantGuardOptions { GuardWrites = false }));
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
    public async Task should_report_invalid_configuration_when_recorded_read_guard_resolves_disabled()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.AddHeadlessTenancy(tenancy => tenancy.EntityFramework(ef => ef.GuardTenantReads()));
        builder.Services.AddSingleton(Options.Create(new TenantGuardOptions { GuardReads = false }));
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
        diagnostics[0].Code.Should().Be("HEADLESS_TENANCY_EF_READ_GUARD_DISABLED");
        diagnostics[0].Seam.Should().Be(HeadlessEntityFrameworkTenancyBuilder.Seam);
    }

    [Fact]
    public async Task should_report_each_recorded_guard_that_resolves_disabled()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.AddHeadlessTenancy(tenancy => tenancy.EntityFramework(ef => ef.GuardTenantWrites().GuardTenantReads()));
        builder.Services.AddSingleton(Options.Create(new TenantGuardOptions()));
        await using var provider = builder.Services.BuildServiceProvider();
        var context = new HeadlessTenancyValidationContext(
            provider,
            provider.GetRequiredService<TenantPostureManifest>()
        );

        var diagnostics = provider
            .GetServices<IHeadlessTenancyValidator>()
            .SelectMany(validator => validator.Validate(context))
            .ToArray();

        diagnostics
            .Select(x => x.Code)
            .Should()
            .BeEquivalentTo("HEADLESS_TENANCY_EF_WRITE_GUARD_DISABLED", "HEADLESS_TENANCY_EF_READ_GUARD_DISABLED");
    }

    [Fact]
    public async Task should_fail_host_startup_when_later_override_disables_read_guard()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.AddHeadlessTenancy(tenancy => tenancy.EntityFramework(ef => ef.GuardTenantReads()));
        builder.Services.AddSingleton(Options.Create(new TenantGuardOptions { GuardReads = false }));
        using var host = builder.Build();

        var act = () => host.StartAsync(AbortToken);

        (await act.Should().ThrowAsync<HeadlessTenancyValidationException>())
            .Which.Message.Should()
            .Contain("HEADLESS_TENANCY_EF_READ_GUARD_DISABLED");
    }

    [Fact]
    public async Task should_enable_read_guard_once_and_record_capability_when_called_twice()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.AddHeadlessTenancy(tenancy =>
            tenancy.EntityFramework(ef => ef.GuardTenantWrites().GuardTenantReads().GuardTenantReads())
        );
        await using var provider = builder.Services.BuildServiceProvider();
        var manifest = provider.GetRequiredService<TenantPostureManifest>();
        var context = new HeadlessTenancyValidationContext(provider, manifest);

        provider.GetRequiredService<IOptions<TenantGuardOptions>>().Value.GuardReads.Should().BeTrue();
        builder.Services.Count(x => x.ServiceType == typeof(HeadlessTenantReadGuardSentinel)).Should().Be(1);
        manifest
            .GetSeam(HeadlessEntityFrameworkTenancyBuilder.Seam)!
            .Capabilities.Should()
            .Contain(HeadlessEntityFrameworkTenancyBuilder.GuardTenantReadsCapabilities)
            .And.Contain(HeadlessEntityFrameworkTenancyBuilder.GuardTenantWritesCapabilities);
        provider
            .GetServices<IHeadlessTenancyValidator>()
            .SelectMany(validator => validator.Validate(context))
            .Should()
            .BeEmpty();
    }

    [Fact]
    public async Task should_fail_with_actionable_error_when_migration_seeder_has_no_context_registration()
    {
        await using var provider = new ServiceCollection().BuildServiceProvider();
        var seeder = new DbMigrationSeeder<UnregisteredDbContext>(provider);

        Func<Task> action = () => seeder.SeedAsync(AbortToken).AsTask();

        var exception = await action.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Contain(nameof(UnregisteredDbContext));
        exception.Which.Message.Should().Contain(nameof(IDbContextFactory<>));
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

    private sealed class UnregisteredDbContext(DbContextOptions<UnregisteredDbContext> options) : DbContext(options);
}
