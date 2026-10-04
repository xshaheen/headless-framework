// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;
using Headless.Jobs;
using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.Infrastructure;

public sealed class JobsDbContextFactoryLifetimeTests : TestBase
{
    [Theory]
    [InlineData(ServiceLifetime.Scoped)]
    [InlineData(ServiceLifetime.Transient)]
    public async Task should_refuse_startup_when_an_application_factory_is_not_a_singleton(ServiceLifetime lifetime)
    {
        // given — Jobs registers its pooled factory with TryAdd, so the application's scoped factory wins and the
        // singleton persistence provider would keep it for the host's life
        var services = _CreateServices();
        services.AddDbContextFactory<TestJobsDbContext>(_UseSqlite, lifetime);
        _AddJobs(services);

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
        violation.Requirement.ServiceType.Should().Be<IDbContextFactory<TestJobsDbContext>>();
        violation.Requirement.RequiredBy.Should().Contain("Jobs");
        violation.Lifetime.Should().Be(lifetime);
    }

    [Fact]
    public async Task should_accept_the_pooled_factory_jobs_registers_itself()
    {
        // given
        var services = _CreateServices();
        _AddJobs(services);

        // when
        var failures = await _RunStartupValidatorsAsync(services);

        // then
        failures.Should().NotContain(failure => failure is InvalidServiceLifetimeException);
        failures
            .OfType<MissingRequiredServiceException>()
            .SelectMany(missing => missing.MissingServices)
            .Should()
            .NotContain(missing => missing.ServiceType == typeof(IDbContextFactory<TestJobsDbContext>));
    }

    private static ServiceCollection _CreateServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(Substitute.For<IJobsOwnerIdentity>());
        services.AddSingleton(new SchedulerOptionsBuilder());

        return services;
    }

    private static void _AddJobs(ServiceCollection services)
    {
        var builder = new JobsEfCoreOptionBuilder<TimeJobEntity, CronJobEntity>();
        ServiceBuilder.UseJobsDbContext<TestJobsDbContext, TimeJobEntity, CronJobEntity>(builder, _UseSqlite);
        builder.ConfigureServices(services);
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

    private sealed class TestJobsDbContext(DbContextOptions<TestJobsDbContext> options)
        : JobsDbContext<TimeJobEntity, CronJobEntity>(options);
}
