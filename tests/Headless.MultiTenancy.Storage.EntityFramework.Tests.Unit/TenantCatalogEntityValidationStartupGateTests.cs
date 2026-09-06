// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.MultiTenancy;
using Headless.MultiTenancy.Internal;
using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore;

namespace Tests;

public sealed class TenantCatalogEntityValidationStartupGateTests : TestBase
{
    [Fact]
    public async Task should_reject_pre_registered_but_unconfigured_tenant_record()
    {
        // given
        var gate = new TenantCatalogEntityValidationStartupGate<PreRegisteredTenantDbContext>(
            new TestDbContextFactory<PreRegisteredTenantDbContext>(() =>
                new PreRegisteredTenantDbContext(_Options<PreRegisteredTenantDbContext>())
            )
        );

        // when
        var act = () => gate.StartingAsync(AbortToken);

        // then
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*AddHeadlessTenancyCatalog*");
    }

    [Fact]
    public async Task should_accept_fully_configured_tenant_record()
    {
        // given
        var gate = new TenantCatalogEntityValidationStartupGate<ConfiguredTenantDbContext>(
            new TestDbContextFactory<ConfiguredTenantDbContext>(() =>
                new ConfiguredTenantDbContext(_Options<ConfiguredTenantDbContext>())
            )
        );

        // when
        var act = () => gate.StartingAsync(AbortToken);

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
            // Registers the entity directly, bypassing AddHeadlessTenancyCatalog, to simulate a consumer
            // who mapped TenantRecord by hand and forgot the Headless configuration call.
            modelBuilder.Entity<TenantRecord>().Ignore(x => x.ExtraProperties);
        }
    }

    private sealed class ConfiguredTenantDbContext(DbContextOptions<ConfiguredTenantDbContext> options)
        : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.AddHeadlessTenancyCatalog(this);
        }
    }
}
