// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs;
using Headless.Jobs.Entities;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Interfaces.Managers;
using Headless.Testing.Tests;
using Headless.UnitOfWork;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.Transactions;

public sealed class UnitOfWorkJobsAccessorTests : TestBase
{
    [Fact]
    public async Task should_bind_the_enlisted_receivers_to_the_unit_when_jobs_is_registered()
    {
        // given — a Jobs host; the factory is the singleton every scope shares.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessJobs(options => options.DisableBackgroundServices());
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var unitOfWork = await provider
            .GetRequiredService<IUnitOfWorkFactory>()
            .BeginAsync(cancellationToken: AbortToken);

        // when
        var scheduler = unitOfWork.Jobs;
        var timeJobs = unitOfWork.TimeJobs<TimeJobEntity>();
        var cronJobs = unitOfWork.CronJobs<CronJobEntity>();

        // then — bound facades, distinct from the injected autonomous singletons.
        scheduler.Should().NotBeSameAs(provider.GetRequiredService<IJobScheduler>());
        timeJobs.Should().NotBeSameAs(provider.GetRequiredService<ITimeJobManager<TimeJobEntity>>());
        cronJobs.Should().NotBeSameAs(provider.GetRequiredService<ICronJobManager<CronJobEntity>>());

        // and — bound once per unit: repeated reads hand back the same receivers, and one facade backs all three.
        unitOfWork.Jobs.Should().BeSameAs(scheduler);
        unitOfWork.TimeJobs<TimeJobEntity>().Should().BeSameAs(timeJobs);
        unitOfWork.CronJobs<CronJobEntity>().Should().BeSameAs(cronJobs);
        timeJobs.Should().BeSameAs(cronJobs);
    }

    [Fact]
    public async Task should_refuse_the_accessor_on_a_unit_that_already_completed()
    {
        // given — the receivers live as unit-local state, which a terminal unit no longer accepts.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessJobs(options => options.DisableBackgroundServices());
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var unitOfWork = await provider
            .GetRequiredService<IUnitOfWorkFactory>()
            .BeginAsync(cancellationToken: AbortToken);
        await unitOfWork.CompleteAsync(AbortToken);

        // when
        var act = () => unitOfWork.Jobs;

        // then
        act.Should().Throw<InvalidOperationException>().WithMessage("*Completed*");
    }

    [Fact]
    public async Task should_name_the_registration_when_jobs_is_not_registered()
    {
        // given — a host with the unit of work but no Jobs setup.
        var services = new ServiceCollection();
        services.AddUnitOfWork();
        await using var provider = services.BuildServiceProvider();
        await using var unitOfWork = await provider
            .GetRequiredService<IUnitOfWorkFactory>()
            .BeginAsync(cancellationToken: AbortToken);

        // when
        var act = () => unitOfWork.Jobs;

        // then
        act.Should().Throw<InvalidOperationException>().WithMessage("*AddHeadlessJobs*");
    }

    [Fact]
    public async Task should_refuse_an_enlisted_schedule_on_a_resource_less_unit_before_any_effect()
    {
        // given — the enlisted receiver over a unit that carries nothing to enlist in: it must refuse rather
        // than write autonomously, and say which receiver would.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessJobs(options => options.DisableBackgroundServices());
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var unitOfWork = await provider
            .GetRequiredService<IUnitOfWorkFactory>()
            .BeginAsync(cancellationToken: AbortToken);
        var job = new TimeJobEntity
        {
            Id = Guid.NewGuid(),
            Function = "accessor-test",
            Description = "accessor-test",
            Request = [],
            ExecutionTime = DateTime.UtcNow.AddHours(1),
        };

        // when
        var act = () => unitOfWork.TimeJobs<TimeJobEntity>().AddAsync(job, AbortToken);

        // then
        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*requires the unit of work to carry a live relational resource*injected scheduler*");
    }

    [Fact]
    public async Task should_name_both_entity_types_when_the_accessor_asks_for_one_the_host_did_not_register()
    {
        // One facade per unit serves the host's entity pair; a receiver for another entity type cannot be bound
        // to it, and the refusal names the registered and the requested types so the mismatch is obvious.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessJobs(options => options.DisableBackgroundServices());
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var unitOfWork = await provider
            .GetRequiredService<IUnitOfWorkFactory>()
            .BeginAsync(cancellationToken: AbortToken);

        var timeJobs = () => unitOfWork.TimeJobs<OtherTimeJob>();
        var cronJobs = () => unitOfWork.CronJobs<OtherCronJob>();

        timeJobs
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage($"*time-job entity '{nameof(TimeJobEntity)}', not '{nameof(OtherTimeJob)}'*");
        cronJobs
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage($"*cron-job entity '{nameof(CronJobEntity)}', not '{nameof(OtherCronJob)}'*");
        unitOfWork.State.Should().Be(UnitOfWorkState.Active, "a refused accessor leaves the unit as it was");
    }

    private sealed class OtherTimeJob : TimeJobEntity<OtherTimeJob>;

    private sealed class OtherCronJob : CronJobEntity;
}
