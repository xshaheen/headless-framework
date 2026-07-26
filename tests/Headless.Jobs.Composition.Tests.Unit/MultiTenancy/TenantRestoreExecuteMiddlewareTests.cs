// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Jobs;
using Headless.Jobs.Base;
using Headless.Jobs.Enums;
using Headless.Jobs.Models;
using Headless.Jobs.MultiTenancy;
using Headless.Testing.Tests;
using Microsoft.Extensions.Options;

namespace Tests.MultiTenancy;

public sealed class TenantRestoreExecuteMiddlewareTests : TestBase
{
    private const string _Function = "tenancy-exec-fn";
    private static readonly JobFunctionDescriptor _Descriptor = new(_Function, null, "", JobPriority.Normal, 0);

    [Fact]
    public async Task handler_observes_the_persisted_tenant()
    {
        var tenant = new TestCurrentTenant();
        var middleware = _Create(tenant, propagate: true);
        string? observed = null;

        await middleware.InvokeAsync(
            _Context("t1"),
            _ =>
            {
                observed = tenant.Id;
                return Task.CompletedTask;
            },
            AbortToken
        );

        observed.Should().Be("t1");
    }

    [Fact]
    public async Task ambient_is_reverted_after_the_attempt_completes()
    {
        var tenant = new TestCurrentTenant();
        var middleware = _Create(tenant, propagate: true);

        using (tenant.Change("outer"))
        {
            await middleware.InvokeAsync(_Context("t1"), _ => Task.CompletedTask, AbortToken);

            tenant.Id.Should().Be("outer");
        }
    }

    [Fact]
    public async Task each_retry_attempt_is_freshly_scoped_with_no_leak_between_attempts()
    {
        var tenant = new TestCurrentTenant();
        var middleware = _Create(tenant, propagate: true);
        var observed = new List<string?>();

        await middleware.InvokeAsync(
            _Context("t1"),
            _ =>
            {
                observed.Add(tenant.Id);
                return Task.CompletedTask;
            },
            AbortToken
        );
        tenant.Id.Should().BeNull("the first attempt scope was disposed");

        await middleware.InvokeAsync(
            _Context("t2"),
            _ =>
            {
                observed.Add(tenant.Id);
                return Task.CompletedTask;
            },
            AbortToken
        );

        observed.Should().Equal("t1", "t2");
        tenant.Id.Should().BeNull();
    }

    [Fact]
    public async Task a_null_tenant_runs_the_attempt_system_scope_even_when_an_ambient_tenant_leaked()
    {
        var tenant = new TestCurrentTenant();
        var middleware = _Create(tenant, propagate: true);
        string? observed = "unset";

        using (tenant.Change("leaked"))
        {
            await middleware.InvokeAsync(
                _Context(tenantId: null),
                _ =>
                {
                    observed = tenant.Id;
                    return Task.CompletedTask;
                },
                AbortToken
            );

            tenant.Id.Should().Be("leaked", "the leaked ambient is restored on scope disposal");
        }

        observed.Should().BeNull();
    }

    [Fact]
    public async Task propagation_disabled_with_no_persisted_tenant_leaves_the_ambient_untouched()
    {
        // A genuinely tenant-free job (null TenantId) on a host with propagation off is a pure pass-through: nothing to
        // restore, so the ambient is left exactly as the caller set it.
        var tenant = new TestCurrentTenant();
        var middleware = _Create(tenant, propagate: false);
        string? observed = null;

        using (tenant.Change("outer"))
        {
            await middleware.InvokeAsync(
                _Context(tenantId: null),
                _ =>
                {
                    observed = tenant.Id;
                    return Task.CompletedTask;
                },
                AbortToken
            );
        }

        observed.Should().Be("outer");
    }

    [Fact]
    public async Task an_explicit_persisted_tenant_is_restored_even_when_propagation_is_off()
    {
        // The schedule side persists an explicit/captured tenant regardless of PropagateTenant, so an explicitly
        // tenanted job MUST run under its tenant even on a host with propagation off — otherwise it silently executes
        // system-scope and tenant-filtered reads cross the boundary (#278 finding #1).
        var tenant = new TestCurrentTenant();
        var middleware = _Create(tenant, propagate: false);
        string? observed = null;

        using (tenant.Change("outer"))
        {
            await middleware.InvokeAsync(
                _Context("t1"),
                _ =>
                {
                    observed = tenant.Id;
                    return Task.CompletedTask;
                },
                AbortToken
            );

            tenant.Id.Should().Be("outer", "the prior ambient is restored on scope disposal");
        }

        observed.Should().Be("t1");
    }

    [Fact]
    public async Task scope_is_reverted_after_a_faulting_attempt()
    {
        var tenant = new TestCurrentTenant();
        var middleware = _Create(tenant, propagate: true);
        var boom = new InvalidOperationException("handler boom");

        using (tenant.Change("outer"))
        {
            var act = () => middleware.InvokeAsync(_Context("t1"), _ => Task.FromException(boom), AbortToken);

            (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Should().BeSameAs(boom);
            tenant.Id.Should().Be("outer");
        }
    }

    [Fact]
    public async Task scope_is_reverted_after_a_cancelled_attempt()
    {
        var tenant = new TestCurrentTenant();
        var middleware = _Create(tenant, propagate: true);

        using (tenant.Change("outer"))
        {
            var act = () =>
                middleware.InvokeAsync(_Context("t1"), _ => Task.FromCanceled(new(canceled: true)), AbortToken);

            await act.Should().ThrowAsync<OperationCanceledException>();
            tenant.Id.Should().Be("outer");
        }
    }

    private static TenantRestoreExecuteMiddleware _Create(ICurrentTenant tenant, bool propagate)
    {
        return new TenantRestoreExecuteMiddleware(
            tenant,
            Options.Create(new JobsTenancyOptions { PropagateTenant = propagate })
        );
    }

    private static JobExecuteContext _Context(string? tenantId)
    {
        var execution = new JobExecutionState { FunctionName = _Function, TenantId = tenantId };
        var functionContext = new JobFunctionContext
        {
            FunctionName = _Function,
            CronOccurrenceOperations = new CronOccurrenceOperations(() => { }),
        };

        return new JobExecuteContext(_Descriptor, execution, functionContext, attempt: 0, NullServiceProvider.Instance);
    }

    // Per-instance AsyncLocal-backed tenant (mirrors CurrentTenant's scope semantics) so these tests exercise real
    // downward flow and revert-on-dispose without sharing the process-global accessor.
    private sealed class TestCurrentTenant : ICurrentTenant
    {
        private readonly AsyncLocal<string?> _id = new();

        public bool IsAvailable => _id.Value is not null;

        public string? Id => _id.Value;

        public string? Name => null;

        public IDisposable Change(string? id, string? name = null)
        {
            var previous = _id.Value;
            _id.Value = id;

            return new Scope(() => _id.Value = previous);
        }

        private sealed class Scope(Action onDispose) : IDisposable
        {
            public void Dispose() => onDispose();
        }
    }
}
