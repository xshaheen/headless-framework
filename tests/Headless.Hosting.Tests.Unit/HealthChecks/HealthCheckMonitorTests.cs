// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Tests.HealthChecks;

public sealed class HealthCheckMonitorTests : TestBase
{
    private static readonly DateTimeOffset _Start = new(2026, 11, 26, 9, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _timeProvider = new(_Start);
    private readonly List<ServiceProvider> _providers = [];

    [Fact]
    public void should_list_registrations_with_tags_failure_status_and_liveness()
    {
        // given
        var sut = _Create(checks =>
        {
            checks.AddCheck("db", new HealthyCheck(), HealthStatus.Degraded, ["ready", "database"]);
            checks.AddCheck("self", static () => HealthCheckResult.Healthy(), tags: ["live"]);
        });

        // when
        var checks = sut.GetChecks();

        // then
        checks.Select(static check => check.Name).Should().Equal("db", "self");
        checks[0].Tags.Should().Equal("database", "ready");
        checks[0].FailureStatus.Should().Be(HealthStatus.Degraded);
        checks[0].IsLiveness.Should().BeFalse();
        checks[1].FailureStatus.Should().Be(HealthStatus.Unhealthy);
        checks[1].IsLiveness.Should().BeTrue();
    }

    [Fact]
    public void should_mark_only_checks_with_a_custom_alive_tag_as_liveness()
    {
        // given
        var sut = _Create(
            checks =>
            {
                checks.AddCheck("self", static () => HealthCheckResult.Healthy(), tags: ["live"]);
                checks.AddCheck("pulse", static () => HealthCheckResult.Healthy(), tags: ["heartbeat"]);
            },
            aliveTag: "heartbeat"
        );

        // when
        var checks = sut.GetChecks();

        // then
        _Descriptor(checks, "self").IsLiveness.Should().BeFalse();
        _Descriptor(checks, "pulse").IsLiveness.Should().BeTrue();
    }

    [Fact]
    public void should_report_a_null_timeout_for_an_infinite_registration_and_the_value_otherwise()
    {
        // given
        var sut = _Create(checks =>
        {
            checks.AddCheck("infinite", static () => HealthCheckResult.Healthy(), timeout: Timeout.InfiniteTimeSpan);
            checks.AddCheck("bounded", static () => HealthCheckResult.Healthy(), timeout: TimeSpan.FromSeconds(5));
        });

        // when
        var checks = sut.GetChecks();

        // then
        _Descriptor(checks, "infinite").Timeout.Should().BeNull();
        _Descriptor(checks, "bounded").Timeout.Should().Be(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task should_neither_list_nor_probe_a_removed_check()
    {
        // given
        var removedRuns = 0;
        var sut = _Create(
            checks =>
            {
                checks.AddCheck("kept", static () => HealthCheckResult.Healthy());
                checks.AddCheck(
                    "removed",
                    () =>
                    {
                        Interlocked.Increment(ref removedRuns);

                        return HealthCheckResult.Healthy();
                    }
                );
            },
            configure: static services =>
                services.RemoveHealthChecks(static registration =>
                    string.Equals(registration.Name, "removed", StringComparison.Ordinal)
                )
        );

        // when
        var report = await sut.ProbeAllAsync(AbortToken);
        var single = await sut.ProbeAsync("removed", AbortToken);

        // then
        sut.GetChecks().Should().ContainSingle().Which.Name.Should().Be("kept");
        report.Entries.Keys.Should().Equal("kept");
        single.Should().BeNull();
        removedRuns.Should().Be(0);
        sut.GetSnapshot().Checks.Should().ContainSingle();
    }

    [Fact]
    public async Task should_record_one_result_per_check_for_a_probe_of_all_checks()
    {
        // given
        var sut = _Create(checks =>
        {
            checks.AddCheck("a", static () => HealthCheckResult.Healthy("all good"));
            checks.AddCheck("b", static () => HealthCheckResult.Degraded("slow"));
        });

        // when
        var report = await sut.ProbeAllAsync(AbortToken);

        // then
        report.Entries.Should().HaveCount(2);
        var snapshot = sut.GetSnapshot();
        var a = _History(snapshot, "a").Results.Should().ContainSingle().Subject;
        a.Status.Should().Be(HealthStatus.Healthy);
        a.Description.Should().Be("all good");
        a.Duration.Should().Be(report.Entries["a"].Duration);
        a.CompletedAt.Should().Be(_Start);
        var b = _History(snapshot, "b").Results.Should().ContainSingle().Subject;
        b.Status.Should().Be(HealthStatus.Degraded);
        b.Description.Should().Be("slow");
    }

    [Fact]
    public async Task should_keep_the_last_30_results_newest_last()
    {
        // given
        var sut = _Create(static checks => checks.AddCheck("a", static () => HealthCheckResult.Healthy()));

        // when
        for (var i = 0; i < 35; i++)
        {
            await sut.ProbeAllAsync(AbortToken);
            _timeProvider.Advance(TimeSpan.FromSeconds(1));
        }

        // then
        var results = sut.GetSnapshot().Checks.Single().Results;
        results.Should().HaveCount(30);
        results[0].CompletedAt.Should().Be(_Start.AddSeconds(5));
        results[^1].CompletedAt.Should().Be(_Start.AddSeconds(34));
    }

    [Fact]
    public async Task should_record_only_the_named_check_for_a_single_probe()
    {
        // given
        var sut = _Create(checks =>
        {
            checks.AddCheck("a", static () => HealthCheckResult.Healthy());
            checks.AddCheck("b", static () => HealthCheckResult.Healthy());
        });

        // when
        var report = await sut.ProbeAsync("a", AbortToken);

        // then
        report.Should().NotBeNull();
        report!.Entries.Keys.Should().Equal("a");
        var snapshot = sut.GetSnapshot();
        _History(snapshot, "a").Results.Should().ContainSingle();
        _History(snapshot, "b").Results.Should().BeEmpty();
    }

    [Fact]
    public async Task should_return_null_and_record_nothing_for_an_unknown_name()
    {
        // given
        var sut = _Create(static checks => checks.AddCheck("a", static () => HealthCheckResult.Healthy()));

        // when
        var report = await sut.ProbeAsync("unknown", AbortToken);

        // then
        report.Should().BeNull();
        sut.GetSnapshot().Checks.Single().Results.Should().BeEmpty();
    }

    [Fact]
    public async Task should_match_a_name_case_insensitively()
    {
        // given
        var sut = _Create(static checks => checks.AddCheck("Alpha", static () => HealthCheckResult.Healthy()));

        // when
        var report = await sut.ProbeAsync("alpha", AbortToken);

        // then
        report.Should().NotBeNull();
        report!.Entries.Should().ContainKey("Alpha");
        sut.GetSnapshot().Checks.Single().Results.Should().ContainSingle();
    }

    [Fact]
    public void should_reject_names_that_differ_only_in_case_as_the_health_check_service_does()
    {
        // given: the history keys names case-insensitively because the health check service treats them that way
        var services = new ServiceCollection();
        services.AddLogging();
        services
            .AddHealthChecks()
            .AddCheck("a", static () => HealthCheckResult.Healthy())
            .AddCheck("A", static () => HealthCheckResult.Healthy());
        using var provider = services.BuildServiceProvider();

        // when
        var act = () => provider.GetRequiredService<HealthCheckService>();

        // then
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task should_store_fixed_text_instead_of_the_message_of_a_throwing_check()
    {
        // given
        var sut = _Create(static checks => checks.AddCheck<ThrowingHealthCheck>("boom"));

        // when
        await sut.ProbeAllAsync(AbortToken);

        // then
        var result = sut.GetSnapshot().Checks.Single().Results.Should().ContainSingle().Subject;
        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Be("The 'boom' health check failed.");
        typeof(HealthCheckProbeResult)
            .GetProperties()
            .Select(static property => property.Name)
            .Should()
            .NotContain(["Exception", "Data"]);
    }

    [Fact]
    public async Task should_report_null_status_before_any_probe_and_the_worst_latest_status_after()
    {
        // given
        var sut = _Create(checks =>
        {
            checks.AddCheck("a", static () => HealthCheckResult.Healthy());
            checks.AddCheck("b", static () => HealthCheckResult.Unhealthy());
            checks.AddCheck("c", static () => HealthCheckResult.Degraded());
        });
        var before = sut.GetSnapshot();

        // when
        await sut.ProbeAllAsync(AbortToken);

        // then
        before.Status.Should().BeNull();
        sut.GetSnapshot().Status.Should().Be(HealthStatus.Unhealthy);
    }

    [Fact]
    public async Task should_ignore_unprobed_checks_in_the_overall_status()
    {
        // given
        var sut = _Create(checks =>
        {
            checks.AddCheck("a", static () => HealthCheckResult.Degraded());
            checks.AddCheck("b", static () => HealthCheckResult.Unhealthy());
            checks.AddCheck("c", static () => HealthCheckResult.Unhealthy());
        });

        // when
        await sut.ProbeAsync("a", AbortToken);

        // then
        sut.GetSnapshot().Status.Should().Be(HealthStatus.Degraded);
    }

    [Fact]
    public async Task should_share_one_run_between_concurrent_probes_of_all_checks()
    {
        // given
        var gate = new GatedCheck();
        var sut = _Create(checks => checks.AddCheck("gated", gate));

        // when
        var first = sut.ProbeAllAsync(AbortToken);
        var second = sut.ProbeAllAsync(AbortToken);
        await gate.Entered.Task.WaitAsync(AbortToken);
        gate.Release.SetResult();
        var reports = await Task.WhenAll(first, second);

        // then
        gate.Runs.Should().Be(1);
        reports[0].Should().BeSameAs(reports[1]);
        sut.GetSnapshot().Checks.Single().Results.Should().ContainSingle();
    }

    [Fact]
    public async Task should_end_only_the_cancelled_callers_wait_while_another_caller_waits()
    {
        // given
        var gate = new GatedCheck();
        var sut = _Create(checks => checks.AddCheck("gated", gate));
        using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);
        var leaving = sut.ProbeAllAsync(cancelled.Token);
        var staying = sut.ProbeAllAsync(AbortToken);
        await gate.Entered.Task.WaitAsync(AbortToken);

        // when
        await cancelled.CancelAsync();
        var leave = async () => await leaving;
        await leave.Should().ThrowAsync<OperationCanceledException>();
        gate.Release.SetResult();
        var report = await staying;

        // then
        report.Status.Should().Be(HealthStatus.Healthy);
        gate.Runs.Should().Be(1);
        sut.GetSnapshot().Checks.Single().Results.Should().ContainSingle();
    }

    [Fact]
    public async Task should_cancel_the_run_and_record_nothing_when_every_caller_cancels()
    {
        // given: the first run hangs until cancelled; later runs complete at once
        var gate = new GatedCheck { HangFirstRunOnly = true };
        var sut = _Create(checks => checks.AddCheck("gated", gate));
        using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);
        var hung = sut.ProbeAllAsync(cancelled.Token);
        await gate.Entered.Task.WaitAsync(AbortToken);

        // when
        await cancelled.CancelAsync();
        var wait = async () => await hung;
        await wait.Should().ThrowAsync<OperationCanceledException>();
        await gate.FirstRunCancelled.Task.WaitAsync(AbortToken);
        var next = await sut.ProbeAllAsync(AbortToken);

        // then: the second call started its own run, and only that run was recorded
        gate.Runs.Should().Be(2);
        next.Status.Should().Be(HealthStatus.Healthy);
        sut.GetSnapshot().Checks.Single().Results.Should().ContainSingle();
    }

    [Fact]
    public async Task should_cancel_an_in_flight_run_on_dispose()
    {
        // given
        var gate = new GatedCheck();
        var sut = _Create(checks => checks.AddCheck("gated", gate));
        var probe = sut.ProbeAllAsync(AbortToken);
        await gate.Entered.Task.WaitAsync(AbortToken);

        // when
        ((IDisposable)sut).Dispose();

        // then
        var wait = async () => await probe;
        await wait.Should().ThrowAsync<OperationCanceledException>();
        await gate.FirstRunCancelled.Task.WaitAsync(AbortToken);
        var probeAfter = async () => await sut.ProbeAllAsync(AbortToken);
        await probeAfter.Should().ThrowAsync<ObjectDisposedException>();
        var singleAfter = async () => await sut.ProbeAsync("gated", AbortToken);
        await singleAfter.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public async Task should_drop_a_slow_bulk_result_that_started_before_a_newer_single_result()
    {
        // given: the bulk run's first call of "a" hangs until released and then fails; a later single probe recovers
        var gate = new GatedCheck { FirstRunStatus = HealthStatus.Unhealthy };
        var sut = _Create(checks =>
        {
            checks.AddCheck("a", gate);
            checks.AddCheck("b", static () => HealthCheckResult.Healthy());
        });
        var bulk = sut.ProbeAllAsync(AbortToken);
        await gate.Entered.Task.WaitAsync(AbortToken);
        _timeProvider.Advance(TimeSpan.FromSeconds(1));
        await sut.ProbeAsync("a", AbortToken);

        // when
        gate.Release.SetResult();
        var report = await bulk;

        // then
        report.Entries["a"].Status.Should().Be(HealthStatus.Unhealthy);
        var snapshot = sut.GetSnapshot();
        _History(snapshot, "a").Results.Should().ContainSingle().Which.Status.Should().Be(HealthStatus.Healthy);
        _History(snapshot, "b").Results.Should().ContainSingle();
    }

    [Fact]
    public async Task should_drop_a_slow_bulk_result_when_a_newer_single_probe_started_at_the_same_instant()
    {
        // given: the clock never moves, so only the start order tells the runs apart
        var gate = new GatedCheck { FirstRunStatus = HealthStatus.Unhealthy };
        var sut = _Create(checks => checks.AddCheck("a", gate));
        var bulk = sut.ProbeAllAsync(AbortToken);
        await gate.Entered.Task.WaitAsync(AbortToken);
        await sut.ProbeAsync("a", AbortToken);

        // when
        gate.Release.SetResult();
        await bulk;

        // then
        _History(sut.GetSnapshot(), "a")
            .Results.Should()
            .ContainSingle()
            .Which.Status.Should()
            .Be(HealthStatus.Healthy);
    }

    [Fact]
    public async Task should_start_a_new_run_after_the_health_check_service_failed()
    {
        // given: a health check service whose first run throws
        var service = new FailingOnceHealthCheckService();
        var sut = _Create(
            static checks => checks.AddCheck("a", static () => HealthCheckResult.Healthy()),
            configure: services => services.Replace(ServiceDescriptor.Singleton<HealthCheckService>(service))
        );
        var failed = sut.ProbeAllAsync(AbortToken);

        // when: the retry is issued from the failed run's own continuation
        var retry = failed
            .ContinueWith(
                _ => sut.ProbeAllAsync(AbortToken),
                AbortToken,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default
            )
            .Unwrap();
        var waitFailed = async () => await failed;
        await waitFailed.Should().ThrowAsync<InvalidOperationException>();
        var report = await retry;

        // then
        report.Status.Should().Be(HealthStatus.Healthy);
        service.Runs.Should().Be(2);
        _History(sut.GetSnapshot(), "a").Results.Should().ContainSingle();
    }

    [Fact]
    public async Task should_not_record_a_single_probe_whose_caller_cancelled()
    {
        // given
        var gate = new GatedCheck();
        var sut = _Create(checks => checks.AddCheck("gated", gate));
        using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);
        var probe = sut.ProbeAsync("gated", cancelled.Token);
        await gate.Entered.Task.WaitAsync(AbortToken);

        // when
        await cancelled.CancelAsync();

        // then
        var wait = async () => await probe;
        await wait.Should().ThrowAsync<OperationCanceledException>();
        sut.GetSnapshot().Checks.Single().Results.Should().BeEmpty();
    }

    [Fact]
    public async Task should_omit_a_registration_that_no_longer_exists_from_the_snapshot()
    {
        // given: a removal that a test flips after the first probe
        var removeB = false;
        var sut = _Create(
            checks =>
            {
                checks.AddCheck("a", static () => HealthCheckResult.Healthy());
                checks.AddCheck("b", static () => HealthCheckResult.Healthy());
            },
            configure: services =>
                services.AddSingleton<IOptions<HealthCheckServiceOptions>>(sp => new FlippingOptions(
                    sp.GetRequiredService<IOptionsFactory<HealthCheckServiceOptions>>(),
                    () => removeB
                ))
        );
        await sut.ProbeAllAsync(AbortToken);

        // when
        removeB = true;
        var snapshot = sut.GetSnapshot();

        // then
        snapshot.Checks.Should().ContainSingle().Which.Descriptor.Name.Should().Be("a");
    }

    [Fact]
    public async Task should_reject_a_blank_name()
    {
        // given
        var sut = _Create(static checks => checks.AddCheck("a", static () => HealthCheckResult.Healthy()));

        // when
        var act = async () => await sut.ProbeAsync(" ", AbortToken);

        // then
        await act.Should().ThrowAsync<ArgumentException>();
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        foreach (var provider in _providers)
        {
            await provider.DisposeAsync();
        }

        await base.DisposeAsyncCore();
    }

    private static HealthCheckHistory _History(HealthCheckSnapshot snapshot, string name)
    {
        return snapshot.Checks.Single(check => string.Equals(check.Descriptor.Name, name, StringComparison.Ordinal));
    }

    private static HealthCheckDescriptor _Descriptor(IReadOnlyList<HealthCheckDescriptor> checks, string name)
    {
        return checks.Single(check => string.Equals(check.Name, name, StringComparison.Ordinal));
    }

    private IHealthCheckMonitor _Create(
        Action<IHealthChecksBuilder> register,
        string aliveTag = "live",
        Action<IServiceCollection>? configure = null
    )
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(_timeProvider);
        register(services.AddHealthChecks());
        configure?.Invoke(services);
        services.AddHeadlessHealthCheckMonitor(aliveTag);
        var provider = services.BuildServiceProvider();
        _providers.Add(provider);

        return provider.GetRequiredService<IHealthCheckMonitor>();
    }

    private sealed class HealthyCheck : IHealthCheck
    {
        public Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default
        )
        {
            return Task.FromResult(HealthCheckResult.Healthy());
        }
    }

    private sealed class ThrowingHealthCheck : IHealthCheck
    {
        public Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default
        )
        {
            throw new InvalidOperationException("connection to secret-host:5432 refused");
        }
    }

    /// <summary>A check whose first run waits for <see cref="Release"/> and observes cancellation.</summary>
    private sealed class GatedCheck : IHealthCheck
    {
        private int _runs;

        public int Runs => Volatile.Read(ref _runs);

        public bool HangFirstRunOnly { get; init; }

        public HealthStatus FirstRunStatus { get; init; } = HealthStatus.Healthy;

        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource FirstRunCancelled { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default
        )
        {
            var run = Interlocked.Increment(ref _runs);

            if (run > 1)
            {
                return HealthCheckResult.Healthy();
            }

            Entered.TrySetResult();

            try
            {
                if (HangFirstRunOnly)
                {
                    await Task.Delay(Timeout.Infinite, cancellationToken);
                }

                await Release.Task.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                FirstRunCancelled.TrySetResult();

                throw;
            }

            return new HealthCheckResult(FirstRunStatus);
        }
    }

    private sealed class FailingOnceHealthCheckService : HealthCheckService
    {
        private int _runs;

        public int Runs => Volatile.Read(ref _runs);

        public override async Task<HealthReport> CheckHealthAsync(
            Func<HealthCheckRegistration, bool>? predicate,
            CancellationToken cancellationToken = default
        )
        {
            // Fails after yielding, so the run completes on a continuation and a caller can retry before cleanup.
            await Task.Yield();

            if (Interlocked.Increment(ref _runs) == 1)
            {
                throw new InvalidOperationException("The health check service failed.");
            }

            var entries = new Dictionary<string, HealthReportEntry>(StringComparer.OrdinalIgnoreCase)
            {
                ["a"] = new(HealthStatus.Healthy, description: null, TimeSpan.Zero, exception: null, data: null),
            };

            return new HealthReport(entries, TimeSpan.Zero);
        }
    }

    private sealed class FlippingOptions(IOptionsFactory<HealthCheckServiceOptions> factory, Func<bool> removeB)
        : IOptions<HealthCheckServiceOptions>
    {
        public HealthCheckServiceOptions Value
        {
            get
            {
                var value = factory.Create(Microsoft.Extensions.Options.Options.DefaultName);

                if (removeB())
                {
                    foreach (
                        var registration in value
                            .Registrations.Where(static r => string.Equals(r.Name, "b", StringComparison.Ordinal))
                            .ToList()
                    )
                    {
                        value.Registrations.Remove(registration);
                    }
                }

                return value;
            }
        }
    }
}
