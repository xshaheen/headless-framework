// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using System.Diagnostics;
using Headless.Sql;
using Headless.Testing.Helpers;
using Headless.Testing.Tests;

namespace Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SqlTelemetryCollection
{
    public const string Name = "Sql telemetry";
}

[Collection(SqlTelemetryCollection.Name)]
public sealed class SqlAutonomousTelemetryTests : TestBase
{
    private const string _Duration = "headless.sql.autonomous.duration";
    private const string _Attempts = "headless.sql.autonomous.attempts";
    private const string _Retries = "headless.sql.autonomous.retries";
    private const string _Operation = "test.call";

    private static readonly string _FaultType = typeof(FakeDbException).FullName!;

    [Fact]
    public async Task should_record_a_successful_call_with_one_attempt_and_no_fault()
    {
        using var recorder = new TelemetryRecorder(SqlDiagnostics.SourceName);

        var result = await SqlAutonomousTransaction.RetryAsync(
            _Operation,
            static (_, _) => Task.FromResult(5),
            TimeProvider.System,
            cancellationToken: AbortToken
        );

        result.Should().Be(5);

        var span = recorder.Activities.Should().ContainSingle().Which;
        span.OperationName.Should().Be("sql.autonomous_transaction");
        span.Status.Should().Be(ActivityStatusCode.Unset);
        span.GetTagItem("headless.sql.operation").Should().Be(_Operation);
        span.GetTagItem("headless.sql.outcome").Should().Be("success");
        span.GetTagItem("headless.sql.attempts").Should().Be(1);
        span.GetTagItem("error.type").Should().BeNull();

        var duration = recorder.Of(_Duration).Should().ContainSingle().Which;
        duration.Unit.Should().Be("ms");
        duration.Value.Should().BeGreaterThanOrEqualTo(0);
        duration
            .Tags.Should()
            .Equal(
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["headless.sql.operation"] = _Operation,
                    ["headless.sql.outcome"] = "success",
                }
            );

        var attempts = recorder.Of(_Attempts).Should().ContainSingle().Which;
        attempts.Value.Should().Be(1);
        attempts
            .Tags.Should()
            .Equal(
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["headless.sql.operation"] = _Operation,
                    ["headless.sql.outcome"] = "success",
                }
            );

        recorder.Of(_Retries).Should().BeEmpty();
    }

    [Fact]
    public async Task should_record_each_retry_with_its_fault_type_and_sqlstate_when_a_transient_fault_is_retried()
    {
        using var recorder = new TelemetryRecorder(SqlDiagnostics.SourceName);
        var runs = 0;

        var result = await SqlAutonomousTransaction.RetryAsync(
            _Operation,
            (_, _) =>
                ++runs == 1 ? throw new FakeDbException(isTransient: false, sqlState: "40P01") : Task.FromResult(runs),
            TimeProvider.System,
            cancellationToken: AbortToken
        );

        result.Should().Be(2);

        var retry = recorder.Of(_Retries).Should().ContainSingle().Which;
        retry.Unit.Should().Be("{retry}");
        retry.Value.Should().Be(1);
        retry
            .Tags.Should()
            .Equal(
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["headless.sql.operation"] = _Operation,
                    ["error.type"] = _FaultType,
                    ["db.response.status_code"] = "40P01",
                }
            );

        recorder.Of(_Attempts).Should().ContainSingle().Which.Value.Should().Be(2);
        recorder.Of(_Duration).Should().ContainSingle().Which.Tags["headless.sql.outcome"].Should().Be("success");

        var span = recorder.Activities.Should().ContainSingle().Which;
        span.GetTagItem("headless.sql.attempts").Should().Be(2);
        var retryEvent = span.Events.Should().ContainSingle().Which;
        retryEvent.Name.Should().Be("headless.sql.retry");
        retryEvent
            .Tags.Should()
            .Contain(
                new KeyValuePair<string, object?>("headless.sql.attempt", 2),
                new KeyValuePair<string, object?>("error.type", _FaultType),
                new KeyValuePair<string, object?>("db.response.status_code", "40P01")
            );
    }

    [Fact]
    public async Task should_report_the_driver_error_number_when_the_fault_has_no_sqlstate()
    {
        using var recorder = new TelemetryRecorder(SqlDiagnostics.SourceName, spans: false);
        var runs = 0;

        await SqlAutonomousTransaction.RetryAsync(
            _Operation,
            (_, _) => ++runs == 1 ? throw new NumberedDbException(1205) : Task.FromResult(runs),
            TimeProvider.System,
            cancellationToken: AbortToken
        );

        recorder
            .Of(_Retries)
            .Should()
            .ContainSingle()
            .Which.Tags.Should()
            .Equal(
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["headless.sql.operation"] = _Operation,
                    ["error.type"] = typeof(NumberedDbException).FullName,
                    ["db.response.status_code"] = "1205",
                }
            );
    }

    [Fact]
    public async Task should_record_a_commit_fault_without_a_retry_when_the_attempt_had_started_its_commit()
    {
        using var recorder = new TelemetryRecorder(SqlDiagnostics.SourceName);

        var act = async () =>
            await SqlAutonomousTransaction.RetryAsync<int>(
                _Operation,
                (attempt, _) =>
                {
                    attempt.MarkCommitStarted();

                    throw new FakeDbException(isTransient: true);
                },
                TimeProvider.System,
                cancellationToken: AbortToken
            );

        await act.Should().ThrowAsync<FakeDbException>();

        _ShouldEndWith(recorder, "commit_fault", attempts: 1);
        recorder.Of(_Retries).Should().BeEmpty("a commit fault surfaces unchanged and is never retried");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task should_record_a_non_transient_fault_without_a_retry(bool isDatabaseFault)
    {
        using var recorder = new TelemetryRecorder(SqlDiagnostics.SourceName);
        Exception fault = isDatabaseFault
            ? new FakeDbException(isTransient: false, sqlState: "23505")
            : new InvalidOperationException("not a database fault");

        var act = async () =>
            await SqlAutonomousTransaction.RetryAsync<int>(
                _Operation,
                (_, _) => throw fault,
                TimeProvider.System,
                cancellationToken: AbortToken
            );

        await act.Should().ThrowAsync<Exception>();

        _ShouldEndWith(recorder, "non_transient", attempts: 1, errorType: fault.GetType().FullName);
        recorder.Of(_Retries).Should().BeEmpty();
    }

    [Fact]
    public async Task should_record_retries_exhausted_when_every_attempt_fails_with_a_transient_fault()
    {
        using var recorder = new TelemetryRecorder(SqlDiagnostics.SourceName);

        var act = async () =>
            await SqlAutonomousTransaction.RetryAsync<int>(
                _Operation,
                (_, _) => throw new FakeDbException(isTransient: false, sqlState: "40001"),
                TimeProvider.System,
                cancellationToken: AbortToken
            );

        await act.Should().ThrowAsync<FakeDbException>();

        _ShouldEndWith(recorder, "retries_exhausted", attempts: 3);
        recorder.Of(_Retries).Should().HaveCount(2).And.OnlyContain(m => m.Value == 1);
        recorder
            .Activities.Single()
            .Events.Select(e =>
                e.Tags.First(t => string.Equals(t.Key, "headless.sql.attempt", StringComparison.Ordinal)).Value
            )
            .Should()
            .Equal(2, 3);
    }

    [Fact]
    public async Task should_record_a_cancellation_when_the_caller_cancelled_before_the_commit()
    {
        using var recorder = new TelemetryRecorder(SqlDiagnostics.SourceName);
        using var cancellation = new CancellationTokenSource();

        var act = async () =>
            await SqlAutonomousTransaction.RetryAsync<int>(
                _Operation,
                async (_, _) =>
                {
                    await cancellation.CancelAsync();

                    throw new FakeDbException(isTransient: true);
                },
                TimeProvider.System,
                cancellationToken: cancellation.Token
            );

        await act.Should().ThrowAsync<FakeDbException>();

        _ShouldEndWith(recorder, "canceled", attempts: 1);
        recorder.Of(_Retries).Should().BeEmpty();
    }

    [Fact]
    public async Task should_run_the_attempt_under_the_call_span_and_restore_the_callers_span_afterwards()
    {
        using var outer = RecordedTestActivity.Start();
        using var recorder = new TelemetryRecorder(SqlDiagnostics.SourceName, metrics: false);
        Activity? inside = null;

        await SqlAutonomousTransaction.RetryAsync(
            _Operation,
            (_, _) =>
            {
                inside = Activity.Current;

                return Task.FromResult(0);
            },
            TimeProvider.System,
            cancellationToken: AbortToken
        );

        var span = recorder.Activities.Should().ContainSingle().Which;
        inside.Should().BeSameAs(span, "driver spans of the attempt nest under the call span");
        span.Parent.Should().BeSameAs(outer.Activity);
        Activity.Current.Should().BeSameAs(outer.Activity, "the call span must not leak into the caller's context");
    }

    [Fact]
    public async Task should_start_no_span_and_record_nothing_when_nothing_listens()
    {
        using var outer = RecordedTestActivity.Start();
        Activity? inside = null;

        await SqlAutonomousTransaction.RetryAsync(
            _Operation,
            (_, _) =>
            {
                inside = Activity.Current;

                return Task.FromResult(0);
            },
            TimeProvider.System,
            cancellationToken: AbortToken
        );

        inside.Should().BeSameAs(outer.Activity, "with no listener the call takes the unobserved path");
    }

    [Fact]
    public async Task should_record_metrics_without_a_span_when_only_metrics_are_subscribed()
    {
        using var outer = RecordedTestActivity.Start();
        using var recorder = new TelemetryRecorder(SqlDiagnostics.SourceName, spans: false);
        Activity? inside = null;

        await SqlAutonomousTransaction.RetryAsync(
            _Operation,
            (_, _) =>
            {
                inside = Activity.Current;

                return Task.FromResult(0);
            },
            TimeProvider.System,
            cancellationToken: AbortToken
        );

        inside.Should().BeSameAs(outer.Activity);
        recorder.Of(_Duration).Should().ContainSingle();
    }

    private static void _ShouldEndWith(
        TelemetryRecorder recorder,
        string outcome,
        int attempts,
        string? errorType = null
    )
    {
        errorType ??= _FaultType;
        var expected = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["headless.sql.operation"] = _Operation,
            ["headless.sql.outcome"] = outcome,
            ["error.type"] = errorType,
        };

        recorder.Of(_Duration).Should().ContainSingle().Which.Tags.Should().Equal(expected);

        var attemptCount = recorder.Of(_Attempts).Should().ContainSingle().Which;
        attemptCount.Value.Should().Be(attempts);
        attemptCount.Tags.Should().Equal(expected);

        var span = recorder.Activities.Should().ContainSingle().Which;
        span.Status.Should().Be(ActivityStatusCode.Error);
        span.StatusDescription.Should().BeNull("a driver message can quote key values");
        span.GetTagItem("headless.sql.operation").Should().Be(_Operation);
        span.GetTagItem("headless.sql.outcome").Should().Be(outcome);
        span.GetTagItem("headless.sql.attempts").Should().Be(attempts);
        span.GetTagItem("error.type").Should().Be(errorType);
    }

    private sealed class FakeDbException(bool isTransient, string? sqlState = null) : DbException("database fault")
    {
        public override bool IsTransient { get; } = isTransient;

        public override string? SqlState { get; } = sqlState;
    }

    // Shaped like SqlClient's exception: no SQLSTATE, the error code in a Number property, and the transient signal
    // left to the classifier (here the driver flag, since the type name is not SqlClient's).
    private sealed class NumberedDbException(int number) : DbException("database fault")
    {
        public int Number { get; } = number;

        public override bool IsTransient => true;
    }
}
