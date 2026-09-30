// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Jobs;
using Headless.Jobs.Base;
using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Headless.Jobs.Exceptions;
using Headless.Jobs.Models;
using Headless.MultiTenancy;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.MultiTenancy;

[Collection<JobsHelperCollection>]
public sealed class JobsTenancyRegistrationTests : TestBase
{
    private static readonly JobFunctionDescriptor _Descriptor = new("any-fn", null, "", JobPriority.Normal, 0);

    [Fact]
    public async Task add_headless_jobs_registers_and_dispatches_the_schedule_tenancy_middleware()
    {
        await using var provider = _BuildHost();

        // A blank explicit tenant is rejected by structural validation only when the middleware actually dispatched.
        var act = () => _DispatchScheduleAsync(provider, _TimeJob(tenantId: "   "));

        await act.Should().ThrowAsync<JobValidatorException>();
    }

    [Fact]
    public async Task a_second_host_in_the_process_builds_its_own_registry_and_still_dispatches()
    {
        await using var firstHost = _BuildHost();
        _ = firstHost.GetRequiredService<JobFunctionRegistry>();

        // Each host freezes its own catalog, so a second host built after the first froze still carries the tenancy
        // middleware and dispatches it.
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

        await provider
            .GetRequiredService<JobFunctionRegistry>()
            .Middleware.DispatchScheduleAsync(
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

        var middleware = provider.GetRequiredService<JobFunctionRegistry>().Middleware;
        await middleware.DispatchScheduleAsync(
            new JobScheduleContext(_Descriptor, _TimeJob(tenantId: null), provider),
            _ =>
            {
                scheduleNext = true;
                return Task.CompletedTask;
            },
            AbortToken
        );

        await middleware.DispatchExecuteAsync(
            new JobExecuteContext(
                _Descriptor,
                new JobExecutionState { FunctionName = _Descriptor.FunctionName },
                new JobContext { FunctionName = _Descriptor.FunctionName },
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
        return provider
            .GetRequiredService<JobFunctionRegistry>()
            .Middleware.DispatchScheduleAsync(
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
