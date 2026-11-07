// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.CompilerServices;
using Headless;
using Headless.Hosting;
using Headless.Jobs;
using Headless.Jobs.Instrumentation;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tests.Execution;

/// <summary>
/// Jobs stores fingerprints from the shared <see cref="FailureFingerprint"/> algorithm, and its fixed fingerprints are
/// constants that must equal what that algorithm produces for each cause, so Jobs groups stay comparable with every
/// other feature's.
/// </summary>
public sealed class JobFailureFingerprintsTests : TestBase
{
    [Fact]
    public void should_share_the_version_prefix_of_the_shared_algorithm()
    {
        JobFailureFingerprints.VersionPrefix.Should().Be(FailureFingerprint.VersionPrefix);
    }

    [Theory]
    [InlineData(JobFailureFingerprints.NodeNotAlive, "node-not-alive")]
    [InlineData(JobFailureFingerprints.LeaseLapsed, "lease-lapsed")]
    [InlineData(JobFailureFingerprints.InterruptedByShutdown, "interrupted-by-shutdown")]
    [InlineData(JobFailureFingerprints.RetryBudgetExhaustedByRecovery, "retry-budget-exhausted-by-recovery")]
    public void should_equal_the_shared_fixed_fingerprint_when_the_constant_names_a_cause(string constant, string cause)
    {
        constant.Should().Be(FailureFingerprint.ForCause(cause));
        constant.Length.Should().BeLessThanOrEqualTo(JobFailureFingerprints.MaxLength);
    }

    [Fact]
    public async Task should_store_the_computed_fingerprint_when_a_job_fails_through_the_handler()
    {
        var written = new List<(JobStatus Status, int RetryCount, string? Fingerprint, bool Carried)>();
        var manager = Substitute.For<IInternalJobManager>();
        manager.RenewLeaseAsync(Arg.Any<JobExecutionState>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(1));
        manager
            .IsTimeJobCancellationRequestedAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<bool?>(false));
        manager
            .UpdateTickerAsync(
                Arg.Do<JobExecutionState>(state =>
                    written.Add(
                        (
                            state.Status,
                            state.RetryCount,
                            state.FailureFingerprint,
                            state.PropertiesToUpdate.Contains(nameof(JobExecutionState.FailureFingerprint))
                        )
                    )
                ),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.FromResult(1));

        var thrown = new List<Exception>();
        var attempt = 0;
        var job = new JobExecutionState
        {
            JobId = Guid.NewGuid(),
            FunctionName = "fingerprinted",
            Type = JobType.TimeJob,
            ExecutionTime = DateTime.UtcNow,
            Retries = 1,
            RetryIntervals = [0],
            Status = JobStatus.Queued,
            ClaimOwner = "node-a@1",
            CachedDelegate = (_, _, _) =>
            {
                // A different exception type on the retry gives the retry write and the final write different causes.
                Exception exception = attempt++ == 0 ? _Catch(() => _ThrowForOrder("order 1")) : _Catch(_ThrowTimeout);
                thrown.Add(exception);
                throw exception;
            },
        };

        var services = new ServiceCollection();
        services.AddSingleton(manager);
        await using var serviceProvider = services.BuildServiceProvider();

        await _Handler(serviceProvider, manager).ExecuteTaskAsync(job, isDue: false, cancellationToken: AbortToken);

        thrown.Should().HaveCount(2);
        var carried = written.Where(x => x.Carried).ToList();
        carried.Should().HaveCount(2, "the retry write and the terminal failure write each carry a fingerprint");
        carried[0].RetryCount.Should().Be(1);
        carried[0].Status.Should().NotBe(JobStatus.Failed);
        carried[0].Fingerprint.Should().Be(FailureFingerprint.Compute(thrown[0]));
        carried[1].Status.Should().Be(JobStatus.Failed);
        carried[1].Fingerprint.Should().Be(FailureFingerprint.Compute(thrown[1]));
        carried[0].Fingerprint.Should().NotBe(carried[1].Fingerprint);
    }

    #region Helpers

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Exception _Catch(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
#pragma warning disable ERP022 // False positive: the caught exception is the helper's return value, which every test asserts on.
            return ex;
#pragma warning restore ERP022
        }

        throw new InvalidOperationException("The action did not throw.");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void _ThrowForOrder(string message) => throw new InvalidOperationException(message);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void _ThrowTimeout() => throw new TimeoutException("timed out after 30 s");

    private static JobsExecutionTaskHandler _Handler(IServiceProvider serviceProvider, IInternalJobManager manager)
    {
        return new JobsExecutionTaskHandler(
            serviceProvider,
            TimeProvider.System,
            Substitute.For<IJobsInstrumentation>(),
            manager,
            JobFunctionRegistryBuilder.Build([], [], []),
            new JobsExecutionCancellationRegistry(),
            new SchedulerOptionsBuilder(),
            NullLogger<JobsExecutionTaskHandler>.Instance,
            new JobsFunctionTagBound(JobFunctionRegistryBuilder.Build([], [], []))
        );
    }

    #endregion
}
