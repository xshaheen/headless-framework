// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.EntityFramework;
using Headless.Jobs;
using Headless.Jobs.Customizer;
using Headless.Jobs.Entities;
using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore;

namespace Tests.Transactions;

/// <summary>
/// UseApplicationDbContext rejects, at the registration call, a context Jobs cannot construct outside a request scope,
/// and accepts any context with the options-only constructor, a HeadlessDbContext included.
/// </summary>
public sealed class ApplicationDbContextRegistrationTests : TestBase
{
    [Fact]
    public void should_accept_application_context_deriving_from_headless_db_context()
    {
        var builder = new JobsEfCoreOptionBuilder<TimeJobEntity, CronJobEntity>();

        var act = () =>
            builder.UseApplicationDbContext<HeadlessApplicationContext>(ConfigurationType.UseModelCustomizer);

        act.Should().NotThrow();
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

    // The shape applications declare: the framework base context, taking only its options. Never instantiated.
    private sealed class HeadlessApplicationContext(DbContextOptions<HeadlessApplicationContext> options)
        : HeadlessDbContext(options)
    {
        public override string? DefaultSchema => null;
    }
}
