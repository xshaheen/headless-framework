// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Jobs;
using Headless.Jobs.Base;
using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Headless.Jobs.Exceptions;
using Headless.Jobs.Models;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.MultiTenancy;

[Collection<JobsHelperCollection>]
public sealed class JobsTenancyRegistrationTests : TestBase, IDisposable
{
    private static readonly JobFunctionDescriptor _Descriptor = new("any-fn", null, "", JobPriority.Normal, 0);

    public JobsTenancyRegistrationTests() => JobFunctionProvider.ResetForTests(discoveryComplete: false);

    public void Dispose() => JobFunctionProvider.ResetForTests();

    [Fact]
    public void the_tenancy_registration_is_reserved_once_per_generation_and_reset_rearms_it()
    {
        JobMiddlewareRegistry.TryReserveTenancyRegistration().Should().BeTrue();
        JobMiddlewareRegistry.TryReserveTenancyRegistration().Should().BeFalse();

        JobFunctionProvider.ResetForTests(discoveryComplete: false);

        JobMiddlewareRegistry.TryReserveTenancyRegistration().Should().BeTrue();
    }

    [Fact]
    public async Task add_headless_jobs_registers_and_dispatches_the_schedule_tenancy_middleware()
    {
        await using var provider = _BuildHost();

        // A blank explicit tenant is rejected by structural validation only when the middleware actually dispatched.
        var act = () => _DispatchScheduleAsync(provider, _TimeJob(tenantId: "   "));

        await act.Should().ThrowAsync<JobValidatorException>();
    }

    [Fact]
    public async Task a_second_add_headless_jobs_after_freeze_does_not_throw_and_the_second_host_still_dispatches()
    {
        await using var firstHost = _BuildHost();

        // The registry is frozen; the second host takes the ExistingCatalog path. It must not throw, and its per-host DI
        // must still resolve and dispatch the tenancy middleware against the shared frozen registry.
        ServiceProvider secondHost = null!;
        var buildSecond = () => secondHost = _BuildHost();
        buildSecond.Should().NotThrow();
        await using var owned = secondHost;

        var act = () => _DispatchScheduleAsync(secondHost, _TimeJob(tenantId: "   "));

        await act.Should().ThrowAsync<JobValidatorException>();
    }

    [Fact]
    public async Task the_schedule_dispatch_no_ops_when_the_middleware_is_not_resolvable()
    {
        await using var provider = _BuildHost();

        // Dispatch through a provider that never registered the middleware type (mirrors JobsManager's EmptyServiceProvider
        // unit path): the hand-written dispatch resolves null and no-ops, so even a blank tenant is not validated.
        await using var emptyServices = new ServiceCollection().BuildServiceProvider();
        var nextCalled = false;

        await JobMiddlewareRegistry.DispatchScheduleAsync(
            new JobScheduleContext(_Descriptor, _TimeJob(tenantId: "   "), emptyServices),
            _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            },
            AbortToken
        );

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public void the_current_tenant_fallback_resolves_a_live_accessor_backed_tenant()
    {
        using var provider = _BuildHost();

        provider.GetRequiredService<ICurrentTenant>().Should().BeOfType<CurrentTenant>();
    }

    [Fact]
    public async Task a_host_without_a_tenancy_seam_schedules_and_executes_without_a_resolution_failure()
    {
        await using var provider = _BuildHost();
        var scheduleNext = false;
        var executeNext = false;

        await JobMiddlewareRegistry.DispatchScheduleAsync(
            new JobScheduleContext(_Descriptor, _TimeJob(tenantId: null), provider),
            _ =>
            {
                scheduleNext = true;
                return Task.CompletedTask;
            },
            AbortToken
        );

        await JobMiddlewareRegistry.DispatchExecuteAsync(
            new JobExecuteContext(
                _Descriptor,
                new JobExecutionState { FunctionName = _Descriptor.FunctionName },
                new JobFunctionContext
                {
                    FunctionName = _Descriptor.FunctionName,
                    CronOccurrenceOperations = new CronOccurrenceOperations(() => { }),
                },
                attempt: 0,
                provider
            ),
            _ =>
            {
                executeNext = true;
                return Task.CompletedTask;
            },
            AbortToken
        );

        scheduleNext.Should().BeTrue();
        executeNext.Should().BeTrue();
    }

    private static Task _DispatchScheduleAsync(IServiceProvider provider, TimeJobEntity job)
    {
        return JobMiddlewareRegistry.DispatchScheduleAsync(
            new JobScheduleContext(_Descriptor, job, provider),
            _ => Task.CompletedTask,
            AbortToken
        );
    }

    private static ServiceProvider _BuildHost()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessJobs(options => options.DisableBackgroundServices());

        return services.BuildServiceProvider();
    }

    private static TimeJobEntity _TimeJob(string? tenantId) =>
        new() { Function = _Descriptor.FunctionName, TenantId = tenantId };
}
