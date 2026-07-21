// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Jobs;
using Headless.Jobs.Enums;
using Headless.Jobs.Instrumentation;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Interfaces.Managers;
using Headless.Jobs.Models;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tests.MultiTenancy;

public sealed class JobsExecutionTaskHandlerTenancyTests : TestBase
{
    [Fact]
    public async Task failure_callbacks_observe_the_job_tenant_scope()
    {
        // #278 finding #9: the exception observer and the exhausted callback run consumer code AFTER the execute
        // middleware's tenant scope has unwound (the handler threw, so the middleware disposed the scope before these
        // callbacks fire). Without the handler re-establishing the job's tenant, tenant-aware alerting or a
        // compensating transaction would run system-scope. Drive a failing tenant-tagged job and assert both callbacks
        // observe ICurrentTenant == the job's tenant, and that the scope does not leak past the handler.
        var tenant = new AsyncLocalTenant();

        var manager = Substitute.For<IInternalJobManager>();
        manager.RenewLeaseAsync(Arg.Any<JobExecutionState>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(1));
        manager
            .UpdateTickerAsync(Arg.Any<JobExecutionState>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(1));
        manager
            .IsTimeJobCancellationRequestedAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<bool?>(false));

        string? observedByExceptionHandler = null;
        string? observedByExhaustedCallback = null;

        var services = new ServiceCollection();
        services.AddSingleton<IJobExceptionHandler>(
            new CapturingExceptionHandler(() => observedByExceptionHandler = tenant.Id)
        );
        await using var serviceProvider = services.BuildServiceProvider();

        var retryOptions = new JobsRetryOptions
        {
            OnExhausted = (_, _) =>
            {
                observedByExhaustedCallback = tenant.Id;

                return Task.CompletedTask;
            },
        };

        var handler = new JobsExecutionTaskHandler(
            serviceProvider,
            TimeProvider.System,
            Substitute.For<IJobsInstrumentation>(),
            manager,
            JobFunctionRegistryBuilder.Build([], [], []),
            new JobsExecutionCancellationRegistry(),
            new SchedulerOptionsBuilder(),
            NullLogger<JobsExecutionTaskHandler>.Instance,
            retryOptions,
            tenant
        );

        var job = new JobExecutionState
        {
            JobId = Guid.NewGuid(),
            FunctionName = "failing-fn",
            Type = JobType.TimeJob,
            ExecutionTime = DateTime.UtcNow,
            Retries = 0,
            RetryIntervals = [0],
            Status = JobStatus.Queued,
            TenantId = "t1",
            CachedDelegate = (_, _, _) => Task.FromException(new InvalidOperationException("boom")),
        };

        await handler.ExecuteTaskAsync(job, isDue: false, cancellationToken: AbortToken);

        observedByExceptionHandler.Should().Be("t1");
        observedByExhaustedCallback.Should().Be("t1");
        tenant.Id.Should().BeNull("the job tenant scope must not leak past the handler");
    }

    [Fact]
    public async Task system_job_failure_callbacks_run_system_scope_even_under_a_leaked_ambient()
    {
        // Final review: a system-scope job (null TenantId) on a propagation-enabled host must not let a leaked
        // ambient tenant reach its failure callbacks — the callbacks get the same explicit null scope the execute
        // middleware gives the handler.
        var tenant = new AsyncLocalTenant();

        var manager = Substitute.For<IInternalJobManager>();
        manager.RenewLeaseAsync(Arg.Any<JobExecutionState>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(1));
        manager
            .UpdateTickerAsync(Arg.Any<JobExecutionState>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(1));
        manager
            .IsTimeJobCancellationRequestedAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<bool?>(false));

        var observed = new List<string?>();

        var services = new ServiceCollection();
        services.AddSingleton<IJobExceptionHandler>(new CapturingExceptionHandler(() => observed.Add(tenant.Id)));
        await using var serviceProvider = services.BuildServiceProvider();

        var handler = new JobsExecutionTaskHandler(
            serviceProvider,
            TimeProvider.System,
            Substitute.For<IJobsInstrumentation>(),
            manager,
            JobFunctionRegistryBuilder.Build([], [], []),
            new JobsExecutionCancellationRegistry(),
            new SchedulerOptionsBuilder(),
            NullLogger<JobsExecutionTaskHandler>.Instance,
            new JobsRetryOptions(),
            tenant,
            Microsoft.Extensions.Options.Options.Create(new JobsTenancyOptions { PropagateTenant = true })
        );

        var job = new JobExecutionState
        {
            JobId = Guid.NewGuid(),
            FunctionName = "failing-fn",
            Type = JobType.TimeJob,
            ExecutionTime = DateTime.UtcNow,
            Retries = 0,
            RetryIntervals = [0],
            Status = JobStatus.Queued,
            TenantId = null,
            CachedDelegate = (_, _, _) => Task.FromException(new InvalidOperationException("boom")),
        };

        using (tenant.Change("leaked"))
        {
            await handler.ExecuteTaskAsync(job, isDue: false, cancellationToken: AbortToken);

            tenant.Id.Should().Be("leaked", "the leaked ambient is restored after every callback scope disposes");
        }

        observed.Should().NotBeEmpty().And.AllSatisfy(id => id.Should().BeNull());
    }

    private sealed class CapturingExceptionHandler(Action onHandle) : IJobExceptionHandler
    {
        public Task HandleExceptionAsync(
            Exception exception,
            Guid jobId,
            JobType jobType,
            CancellationToken cancellationToken = default
        )
        {
            onHandle();

            return Task.CompletedTask;
        }

        public Task HandleCanceledExceptionAsync(
            Exception exception,
            Guid jobId,
            JobType jobType,
            CancellationToken cancellationToken = default
        )
        {
            return Task.CompletedTask;
        }
    }

    // Per-instance AsyncLocal-backed tenant (mirrors CurrentTenant's scope semantics) so the test exercises real
    // downward flow and revert-on-dispose without sharing the process-global accessor.
    private sealed class AsyncLocalTenant : ICurrentTenant
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
