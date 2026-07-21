// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Jobs;
using Headless.Jobs.Entities;
using Headless.Jobs.Entities.BaseEntity;
using Headless.Jobs.Enums;
using Headless.Jobs.Exceptions;
using Headless.Jobs.Models;
using Headless.Jobs.MultiTenancy;
using Headless.Testing.Tests;
using Microsoft.Extensions.Options;

namespace Tests.MultiTenancy;

public sealed class TenantPropagationScheduleMiddlewareTests : TestBase
{
    private const string _Function = "tenancy-fn";
    private static readonly JobFunctionDescriptor _Descriptor = new(_Function, null, "", JobPriority.Normal, 0);

    [Fact]
    public async Task explicit_tenant_wins_over_a_different_ambient_tenant()
    {
        var job = new TimeJobEntity { Function = _Function, TenantId = "explicit" };

        var nextCalled = await _InvokeAsync(job, ambientTenant: "ambient", propagate: true);

        nextCalled.Should().BeTrue();
        job.TenantId.Should().Be("explicit");
    }

    [Fact]
    public async Task ambient_is_captured_when_no_explicit_tenant_and_propagation_enabled()
    {
        var job = new TimeJobEntity { Function = _Function };

        var nextCalled = await _InvokeAsync(job, ambientTenant: "t1", propagate: true);

        nextCalled.Should().BeTrue();
        job.TenantId.Should().Be("t1");
    }

    [Fact]
    public async Task propagation_disabled_does_not_capture_the_ambient_tenant()
    {
        var job = new TimeJobEntity { Function = _Function };

        var nextCalled = await _InvokeAsync(job, ambientTenant: "t1", propagate: false);

        nextCalled.Should().BeTrue();
        job.TenantId.Should().BeNull();
    }

    [Fact]
    public async Task strict_mode_rejects_a_tenantless_non_system_job()
    {
        var job = new TimeJobEntity { Function = _Function };

        var act = () => _InvokeAsync(job, ambientTenant: null, propagate: true, strict: true);

        await act.Should().ThrowAsync<MissingTenantContextException>();
    }

    [Fact]
    public async Task strict_disabled_keeps_a_tenantless_job_without_rejection()
    {
        var job = new TimeJobEntity { Function = _Function };

        var nextCalled = await _InvokeAsync(job, ambientTenant: null, propagate: false, strict: false);

        nextCalled.Should().BeTrue();
        job.TenantId.Should().BeNull();
    }

    [Fact]
    public async Task system_job_persists_a_null_tenant_and_does_not_reject()
    {
        var job = new TimeJobEntity { Function = _Function, IsSystemJob = true };

        var nextCalled = await _InvokeAsync(job, ambientTenant: null, propagate: true, strict: true);

        nextCalled.Should().BeTrue();
        job.TenantId.Should().BeNull();
    }

    [Fact]
    public async Task system_job_with_an_ambient_tenant_is_rejected()
    {
        var job = new TimeJobEntity { Function = _Function, IsSystemJob = true };

        var act = () => _InvokeAsync(job, ambientTenant: "t1", propagate: true);

        await act.Should().ThrowAsync<JobValidatorException>();
    }

    [Fact]
    public async Task system_job_with_an_explicit_tenant_is_rejected()
    {
        var job = new TimeJobEntity
        {
            Function = _Function,
            IsSystemJob = true,
            TenantId = "t1",
        };

        var act = () => _InvokeAsync(job, ambientTenant: null, propagate: true);

        await act.Should().ThrowAsync<JobValidatorException>();
    }

    [Fact]
    public async Task cron_definition_carrying_a_tenant_is_rejected()
    {
        var cron = new CronJobEntity { Function = _Function, TenantId = "t1" };

        var act = () => _InvokeAsync(cron, ambientTenant: null, propagate: true);

        await act.Should().ThrowAsync<JobValidatorException>();
    }

    [Fact]
    public async Task cron_definition_never_captures_the_ambient_tenant()
    {
        var cron = new CronJobEntity { Function = _Function };

        var nextCalled = await _InvokeAsync(cron, ambientTenant: "t1", propagate: true);

        nextCalled.Should().BeTrue();
        cron.TenantId.Should().BeNull();
    }

    [Fact]
    public async Task over_length_explicit_tenant_is_rejected()
    {
        var job = new TimeJobEntity
        {
            Function = _Function,
            TenantId = new string('x', JobsTenancyOptions.TenantIdMaxLength + 1),
        };

        var act = () => _InvokeAsync(job, ambientTenant: null, propagate: false);

        await act.Should().ThrowAsync<JobValidatorException>();
    }

    [Fact]
    public async Task over_length_ambient_tenant_is_rejected()
    {
        var job = new TimeJobEntity { Function = _Function };

        var act = () =>
            _InvokeAsync(
                job,
                ambientTenant: new string('x', JobsTenancyOptions.TenantIdMaxLength + 1),
                propagate: true
            );

        await act.Should().ThrowAsync<JobValidatorException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task blank_explicit_tenant_is_rejected(string blank)
    {
        var job = new TimeJobEntity { Function = _Function, TenantId = blank };

        var act = () => _InvokeAsync(job, ambientTenant: null, propagate: false);

        await act.Should().ThrowAsync<JobValidatorException>();
    }

    [Fact]
    public async Task whitespace_ambient_tenant_is_rejected()
    {
        var job = new TimeJobEntity { Function = _Function };

        var act = () => _InvokeAsync(job, ambientTenant: "   ", propagate: true);

        await act.Should().ThrowAsync<JobValidatorException>();
    }

    private async Task<bool> _InvokeAsync(BaseJobEntity job, string? ambientTenant, bool propagate, bool strict = false)
    {
        var tenant = Substitute.For<ICurrentTenant>();
        tenant.Id.Returns(ambientTenant);
        var options = Options.Create(
            new JobsTenancyOptions { PropagateTenant = propagate, TenantContextRequired = strict }
        );
        var middleware = new TenantPropagationScheduleMiddleware(tenant, options);
        var context = new JobScheduleContext(_Descriptor, job, NullServiceProvider.Instance);
        var nextCalled = false;

        await middleware.InvokeAsync(
            context,
            _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            },
            AbortToken
        );

        return nextCalled;
    }

    private sealed class NullServiceProvider : IServiceProvider
    {
        public static readonly NullServiceProvider Instance = new();

        public object? GetService(Type serviceType) => null;
    }
}
