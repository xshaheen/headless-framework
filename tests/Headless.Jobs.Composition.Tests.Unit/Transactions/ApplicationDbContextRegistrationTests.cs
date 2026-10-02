// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.EntityFramework;
using Headless.Jobs;
using Headless.Jobs.Customizer;
using Headless.Jobs.Entities;
using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore;

namespace Tests.Transactions;

/// <summary>
/// UseApplicationDbContext must reject, at the registration call, a context Jobs cannot construct outside a request
/// scope, and name the supported alternative. A HeadlessDbContext is the case applications actually hit: its only
/// constructor takes request-scoped services.
/// </summary>
public sealed class ApplicationDbContextRegistrationTests : TestBase
{
    [Fact]
    public void should_throw_naming_use_jobs_db_context_when_application_context_derives_from_headless_db_context()
    {
        var builder = new JobsEfCoreOptionBuilder<TimeJobEntity, CronJobEntity>();

        var act = () =>
            builder.UseApplicationDbContext<HeadlessApplicationContext>(ConfigurationType.UseModelCustomizer);

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*HeadlessApplicationContext*HeadlessDbContext*UseJobsDbContext*");
    }

    [Fact]
    public void should_throw_when_application_context_has_no_options_only_constructor()
    {
        var builder = new JobsEfCoreOptionBuilder<TimeJobEntity, CronJobEntity>();

        var act = () =>
            builder.UseApplicationDbContext<MissingOptionsCtorContext>(ConfigurationType.UseModelCustomizer);

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*MissingOptionsCtorContext*single DbContextOptions<MissingOptionsCtorContext>*");
    }

    [Fact]
    public void should_accept_application_context_with_options_only_constructor()
    {
        var builder = new JobsEfCoreOptionBuilder<TimeJobEntity, CronJobEntity>();

        var act = () => builder.UseApplicationDbContext<WellFormedContext>(ConfigurationType.UseModelCustomizer);

        act.Should().NotThrow();
    }

    private sealed class WellFormedContext(DbContextOptions<WellFormedContext> options) : DbContext(options);

    // Only the implicit parameterless constructor. Never instantiated: the check is reflection only.
    private sealed class MissingOptionsCtorContext : DbContext;

    // The shape applications declare: the framework base context with its required services. Never instantiated.
    private sealed class HeadlessApplicationContext(
        HeadlessDbContextServices services,
        DbContextOptions<HeadlessApplicationContext> options
    ) : HeadlessDbContext(services, options)
    {
        public override string? DefaultSchema => null;
    }
}
