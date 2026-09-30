// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting.DependencyInjection;
using Headless.Hosting.Validation;
using Headless.MultiTenancy;
using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests;

public sealed class EntityFrameworkTenantCatalogFactoryLifetimeTests : TestBase
{
    [Theory]
    [InlineData(ServiceLifetime.Scoped)]
    [InlineData(ServiceLifetime.Transient)]
    public async Task should_refuse_startup_when_the_db_context_factory_is_not_a_singleton(ServiceLifetime lifetime)
    {
        // given — the store is a singleton, so it would keep the first factory it was handed for the host's life
        var builder = _CreateBuilder();
        builder.Services.AddDbContextFactory<CatalogDbContext>(_UseSqlite, lifetime);

        // when
        var failures = await _RunStartupValidatorsAsync(builder);

        // then
        var violation = failures
            .OfType<InvalidServiceLifetimeException>()
            .Should()
            .ContainSingle()
            .Which.Violations.Should()
            .ContainSingle()
            .Which;
        violation.Requirement.ServiceType.Should().Be<IDbContextFactory<CatalogDbContext>>();
        violation.Requirement.RequiredBy.Should().Contain("tenant catalog");
        violation.Lifetime.Should().Be(lifetime);
    }

    [Fact]
    public async Task should_accept_a_singleton_db_context_factory()
    {
        // given
        var builder = _CreateBuilder();
        builder.Services.AddDbContextFactory<CatalogDbContext>(_UseSqlite);

        // when
        var failures = await _RunStartupValidatorsAsync(builder);

        // then — the store's own checks pass; the missing caching provider this project does not install is not
        // part of what the store requires
        failures.Should().NotContain(failure => failure is InvalidServiceLifetimeException);
        failures
            .OfType<MissingRequiredServiceException>()
            .SelectMany(missing => missing.MissingServices)
            .Should()
            .NotContain(missing => missing.ServiceType == typeof(IDbContextFactory<CatalogDbContext>));
        failures.Should().NotContain(failure => failure.Message.Contains("AddHeadlessTenancyCatalog"));
    }

    [Fact]
    public async Task should_report_a_missing_db_context_factory_as_a_missing_required_service()
    {
        // given
        var builder = _CreateBuilder();

        // when
        var failures = await _RunStartupValidatorsAsync(builder);

        // then
        failures
            .OfType<MissingRequiredServiceException>()
            .Should()
            .ContainSingle()
            .Which.MissingServices.Should()
            .Contain(missing => missing.ServiceType == typeof(IDbContextFactory<CatalogDbContext>));
        failures.Should().NotContain(failure => failure is InvalidServiceLifetimeException);
    }

    private static HostApplicationBuilder _CreateBuilder()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.AddHeadlessTenancy(tenancy =>
            tenancy.Catalog(catalog => catalog.UseEntityFramework<CatalogDbContext>())
        );

        return builder;
    }

    private static void _UseSqlite(DbContextOptionsBuilder options)
    {
        options.UseSqlite("Data Source=:memory:");
    }

    /// <summary>
    /// Runs every registered startup validator and collects its failure. Other features' checks (a missing caching
    /// provider, for instance) may fail too; each test asserts only on the failures it is about.
    /// </summary>
    private static async Task<List<Exception>> _RunStartupValidatorsAsync(HostApplicationBuilder builder)
    {
        await using var provider = builder.Services.BuildServiceProvider();
        var failures = new List<Exception>();

        foreach (var validator in provider.GetServices<IHeadlessStartupValidator>())
        {
            try
            {
                await validator.ValidateAsync(AbortToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                failures.Add(exception);
            }
        }

        return failures;
    }

    private sealed class CatalogDbContext(DbContextOptions<CatalogDbContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.AddHeadlessTenancyCatalog(this);
        }
    }
}
