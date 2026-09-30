// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting.DependencyInjection;
using Headless.Hosting.Validation;
using Headless.Permissions;
using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.Setup;

public sealed class EntityFrameworkPermissionsFactoryLifetimeTests : TestBase
{
    [Theory]
    [InlineData(ServiceLifetime.Scoped)]
    [InlineData(ServiceLifetime.Transient)]
    public async Task should_refuse_startup_when_the_db_context_factory_is_not_a_singleton(ServiceLifetime lifetime)
    {
        // given — the EF repositories are singletons, so they would keep the first factory for the host's life
        var services = _CreateServices();
        services.AddDbContextFactory<PermissionsDbContext>(_UseSqlite, lifetime);

        // when
        var failures = await _RunStartupValidatorsAsync(services);

        // then
        var violation = failures
            .OfType<InvalidServiceLifetimeException>()
            .Should()
            .ContainSingle()
            .Which.Violations.Should()
            .ContainSingle()
            .Which;
        violation.Requirement.ServiceType.Should().Be<IDbContextFactory<PermissionsDbContext>>();
        violation.Requirement.RequiredBy.Should().Contain("permissions");
        violation.Lifetime.Should().Be(lifetime);
    }

    [Fact]
    public async Task should_accept_a_singleton_db_context_factory()
    {
        // given
        var services = _CreateServices();
        services.AddDbContextFactory<PermissionsDbContext>(_UseSqlite);

        // when
        var failures = await _RunStartupValidatorsAsync(services);

        // then — the storage's own checks pass; unrelated prerequisites this test does not install may still fail
        failures.Should().NotContain(failure => failure is InvalidServiceLifetimeException);
        failures
            .OfType<MissingRequiredServiceException>()
            .SelectMany(missing => missing.MissingServices)
            .Should()
            .NotContain(missing => missing.ServiceType == typeof(IDbContextFactory<PermissionsDbContext>));
        failures.Should().NotContain(failure => failure.Message.Contains("AddHeadlessPermissions"));
    }

    [Fact]
    public async Task should_report_a_missing_db_context_factory_as_a_missing_required_service()
    {
        // given
        var services = _CreateServices();

        // when
        var failures = await _RunStartupValidatorsAsync(services);

        // then
        failures
            .OfType<MissingRequiredServiceException>()
            .SelectMany(missing => missing.MissingServices)
            .Should()
            .Contain(missing => missing.ServiceType == typeof(IDbContextFactory<PermissionsDbContext>));
        // The entity validator leaves the missing factory to the required-service check, so it must not add a
        // failure of its own (a dropped guard would surface here as a NullReferenceException).
        failures.Should().AllSatisfy(failure => failure.Should().BeOfType<MissingRequiredServiceException>());
    }

    private static ServiceCollection _CreateServices()
    {
        var services = new ServiceCollection();
        services.AddHeadlessPermissions(setup => setup.UseEntityFramework<PermissionsDbContext>());

        return services;
    }

    private static void _UseSqlite(DbContextOptionsBuilder options)
    {
        options.UseSqlite("Data Source=:memory:");
    }

    private static async Task<List<Exception>> _RunStartupValidatorsAsync(ServiceCollection services)
    {
        await using var provider = services.BuildServiceProvider();
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

    private sealed class PermissionsDbContext(DbContextOptions<PermissionsDbContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.AddHeadlessPermissions(this);
        }
    }
}
