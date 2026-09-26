// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting.DependencyInjection;
using Headless.Hosting.Validation;
using Headless.Security;
using Headless.Settings;
using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.Setup;

public sealed class EntityFrameworkSettingsFactoryLifetimeTests : TestBase
{
    [Theory]
    [InlineData(ServiceLifetime.Scoped)]
    [InlineData(ServiceLifetime.Transient)]
    public async Task should_refuse_startup_when_the_db_context_factory_is_not_a_singleton(ServiceLifetime lifetime)
    {
        // given — the EF repositories are singletons, so they would keep the first factory for the host's life
        var services = _CreateServices();
        services.AddDbContextFactory<SettingsDbContext>(_UseSqlite, lifetime);

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
        violation.Requirement.ServiceType.Should().Be<IDbContextFactory<SettingsDbContext>>();
        violation.Requirement.RequiredBy.Should().Contain("settings");
        violation.Lifetime.Should().Be(lifetime);
    }

    [Fact]
    public async Task should_accept_a_singleton_db_context_factory()
    {
        // given
        var services = _CreateServices();
        services.AddDbContextFactory<SettingsDbContext>(_UseSqlite);

        // when
        var failures = await _RunStartupValidatorsAsync(services);

        // then — the storage's own checks pass; unrelated prerequisites this test does not install may still fail
        failures.Should().NotContain(failure => failure is InvalidServiceLifetimeException);
        failures
            .OfType<MissingRequiredServiceException>()
            .SelectMany(missing => missing.MissingServices)
            .Should()
            .NotContain(missing => missing.ServiceType == typeof(IDbContextFactory<SettingsDbContext>));
        failures.Should().NotContain(failure => failure.Message.Contains("AddHeadlessSettings"));
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
            .Contain(missing => missing.ServiceType == typeof(IDbContextFactory<SettingsDbContext>));
        failures.Should().NotContain(failure => failure is InvalidServiceLifetimeException);
    }

    private static ServiceCollection _CreateServices()
    {
        var services = new ServiceCollection();
        // AddHeadlessSettings auto-registers the management core, which requires IStringEncryptionService.
        services.AddStringEncryptionService(options =>
        {
            options.DefaultPassPhrase = "TestPassPhrase123456";
            options.DefaultSalt = [.. "TestSalt"u8];
        });
        services.AddHeadlessSettings(setup => setup.UseEntityFramework<SettingsDbContext>());

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

    private sealed class SettingsDbContext(DbContextOptions<SettingsDbContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.AddHeadlessSettings(this);
        }
    }
}
