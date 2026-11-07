// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Headless.Hosting;
using Headless.Jobs;
using Headless.Jobs.Instrumentation;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tests.Execution;

/// <summary>
/// The failure fingerprint groups failures by cause: the innermost exception type, after unwrapping
/// <see cref="AggregateException"/> and <see cref="TargetInvocationException"/>, plus the top three stack frames outside
/// <c>System.*</c> and <c>Microsoft.*</c>, each as a method full name with compiler-generated names reduced to their
/// declaring method, hashed with SHA-256 and truncated to 16 hex characters behind a <c>v1:</c> prefix.
/// </summary>
public sealed class FailureFingerprintTests : TestBase
{
    private const string _Self = "Tests.Execution.FailureFingerprintTests";

    [Fact]
    public void should_produce_one_fingerprint_when_the_same_failure_carries_different_ids_in_its_message()
    {
        var first = _Catch(() => _ThrowForOrder("order 4f1c-11"));
        var second = _Catch(() => _ThrowForOrder("order 9a2b-77"));

        first.Message.Should().NotBe(second.Message);
        var fingerprint = FailureFingerprint.Compute(first);

        fingerprint.Should().Be(FailureFingerprint.Compute(second));
        fingerprint
            .Should()
            .Be(
                _Expected(
                    "System.InvalidOperationException",
                    $"{_Self}._ThrowForOrder",
                    // The lambda passed to _Catch reduces to the test method that declares it.
                    $"{_Self}.should_produce_one_fingerprint_when_the_same_failure_carries_different_ids_in_its_message",
                    $"{_Self}._Catch"
                )
            );
    }

    [Fact]
    public void should_fingerprint_the_innermost_exception_when_it_is_wrapped_in_aggregate_and_target_invocation()
    {
        var timeout = _Catch(_ThrowTimeout);
        var wrapped = new AggregateException(new TargetInvocationException(timeout));

        var fingerprint = FailureFingerprint.Compute(wrapped);

        fingerprint.Should().Be(FailureFingerprint.Compute(timeout));
        fingerprint.Should().Be(_Expected("System.TimeoutException", $"{_Self}._ThrowTimeout", $"{_Self}._Catch"));
    }

    [Fact]
    public void should_skip_system_frames_and_keep_only_the_top_three_application_frames()
    {
        // int.Parse throws from System.Number, two System frames below the first application frame.
        var exception = _Catch(() => _Outer("not a number"));

        exception.StackTrace.Should().Contain("System.Number");
        FailureFingerprint
            .Compute(exception)
            .Should()
            .Be(_Expected("System.FormatException", $"{_Self}._Parse", $"{_Self}._Middle", $"{_Self}._Outer"));
    }

    [Fact]
    public async Task should_reduce_async_state_machine_and_lambda_frames_to_their_declaring_method()
    {
        Func<Task> lambda = async () => await _FailAsync("run 1");

        Exception? caught = null;
        try
        {
            await lambda();
        }
        catch (InvalidOperationException ex)
        {
            caught = ex;
        }

        caught.Should().NotBeNull();
        caught!.StackTrace.Should().Contain("MoveNext");
        FailureFingerprint
            .Compute(caught)
            .Should()
            .Be(
                _Expected(
                    "System.InvalidOperationException",
                    $"{_Self}._FailAsync",
                    $"{_Self}.should_reduce_async_state_machine_and_lambda_frames_to_their_declaring_method",
                    $"{_Self}.should_reduce_async_state_machine_and_lambda_frames_to_their_declaring_method"
                )
            );
    }

    [Fact]
    public void should_fingerprint_the_type_alone_when_the_exception_was_never_thrown()
    {
        FailureFingerprint
            .Compute(new InvalidOperationException("never thrown"))
            .Should()
            .Be(_Expected("System.InvalidOperationException"));
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

    private static string _Expected(string exceptionType, params string[] frames)
    {
        var input = string.Join('\n', [exceptionType, .. frames]);
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(input)));

        return "v1:" + hash[..16];
    }

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

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void _Outer(string value) => _Middle(value);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void _Middle(string value) => _Parse(value);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void _Parse(string value) => _ = int.Parse(value, CultureInfo.InvariantCulture);

    private static async Task _FailAsync(string id)
    {
        await Task.Yield();

        throw new InvalidOperationException($"failed {id}");
    }

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
