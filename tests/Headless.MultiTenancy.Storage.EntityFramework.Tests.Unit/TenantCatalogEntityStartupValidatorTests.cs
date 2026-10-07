// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.MultiTenancy;
using Headless.MultiTenancy.Internal;
using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class TenantCatalogEntityStartupValidatorTests : TestBase
{
    [Fact]
    public async Task should_reject_pre_registered_but_unconfigured_tenant_record()
    {
        // given
        await using var services = _Services<PreRegisteredTenantDbContext>(() =>
            new PreRegisteredTenantDbContext(_Options<PreRegisteredTenantDbContext>())
        );
        var validator = new TenantCatalogEntityStartupValidator<PreRegisteredTenantDbContext>(services);

        // when
        var act = () => validator.ValidateAsync(AbortToken);

        // then
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*ConfigureHeadlessTenancyCatalog*");
    }

    [Fact]
    public async Task should_accept_fully_configured_tenant_record()
    {
        // given
        await using var services = _Services<ConfiguredTenantDbContext>(() =>
            new ConfiguredTenantDbContext(_Options<ConfiguredTenantDbContext>())
        );
        var validator = new TenantCatalogEntityStartupValidator<ConfiguredTenantDbContext>(services);

        // when
        var act = () => validator.ValidateAsync(AbortToken);

        // then
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task should_leave_a_missing_factory_to_the_required_service_check()
    {
        // given — no factory registered: the required-service check reports it, so this check must not fail first
        await using var services = new ServiceCollection().BuildServiceProvider();
        var validator = new TenantCatalogEntityStartupValidator<ConfiguredTenantDbContext>(services);

        // when
        var act = () => validator.ValidateAsync(AbortToken);

        // then
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public void should_set_is_configured_annotation_when_model_builder_extension_is_applied()
    {
        // given & when
        using var db = new ConfiguredTenantDbContext(_Options<ConfiguredTenantDbContext>());
        var annotation = db.Model.FindAnnotation(TenantCatalogStorageModelAnnotations.IsConfigured);

        // then
        annotation.Should().NotBeNull();
        annotation!.Value.Should().Be(true);
    }

    private static DbContextOptions<TContext> _Options<TContext>()
        where TContext : DbContext
    {
        return new DbContextOptionsBuilder<TContext>().UseSqlite("Data Source=:memory:").Options;
    }

    private static ServiceProvider _Services<TContext>(Func<TContext> createContext)
        where TContext : DbContext
    {
        return new ServiceCollection()
            .AddSingleton<IDbContextFactory<TContext>>(new TestDbContextFactory<TContext>(createContext))
            .BuildServiceProvider();
    }

    private sealed class TestDbContextFactory<TContext>(Func<TContext> createContext) : IDbContextFactory<TContext>
        where TContext : DbContext
    {
        public TContext CreateDbContext()
        {
            return createContext();
        }
    }

    private sealed class PreRegisteredTenantDbContext(DbContextOptions<PreRegisteredTenantDbContext> options)
        : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Registers the entity directly, bypassing ConfigureHeadlessTenancyCatalog, to simulate a consumer
            // who mapped TenantRecord by hand and forgot the Headless configuration call.
            modelBuilder.Entity<TenantRecord>().Ignore(x => x.ExtraProperties);
        }
    }

    private sealed class ConfiguredTenantDbContext(DbContextOptions<ConfiguredTenantDbContext> options)
        : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ConfigureHeadlessTenancyCatalog(this);
        }
    }
}
