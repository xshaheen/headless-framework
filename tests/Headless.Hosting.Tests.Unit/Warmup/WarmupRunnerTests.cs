// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Tests.Warmup;

public sealed class WarmupRunnerTests : TestBase
{
    [Fact]
    public async Task should_start_the_host_without_waiting_and_report_unready_until_a_gating_warmup_finishes()
    {
        // given
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var host = _BuildHost(services => services.AddWarmup(_ => new BlockingWarmup("Cache", gate.Task)));

        // when
        await host.StartAsync(AbortToken);

        // then
        var before = await _CheckAsync(host.Services);
        before.Status.Should().Be(HealthStatus.Unhealthy);
        before.Exception!.Message.Should().Contain("Cache");
        _Runner(host).IsInitialized.Should().BeFalse();

        gate.SetResult();
        await _Runner(host).WaitForInitializationAsync(AbortToken).WaitAsync(TimeSpan.FromSeconds(10), AbortToken);
        (await _CheckAsync(host.Services)).Status.Should().Be(HealthStatus.Healthy);
        await host.StopAsync(AbortToken);
    }

    [Fact]
    public async Task should_retry_a_failed_warmup_and_report_its_last_error_meanwhile()
    {
        // given
        var warmup = new FlakyWarmup("Store", failures: 1);
        using var host = _BuildHost(services => services.AddWarmup(_ => warmup));

        // when
        await host.StartAsync(AbortToken);
        await warmup.FirstFailure.WaitAsync(TimeSpan.FromSeconds(10), AbortToken);

        // The runner records the error just after the attempt returns, so read it once it is there.
        var meanwhile = await _CheckAsync(host.Services);

        for (var i = 0; i < 50 && !_HasError(meanwhile); i++)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10), AbortToken);
            meanwhile = await _CheckAsync(host.Services);
        }

        await _Runner(host).WaitForInitializationAsync(AbortToken).WaitAsync(TimeSpan.FromSeconds(30), AbortToken);

        // then
        meanwhile.Exception!.Message.Should().Contain("Store (last error: The store is not reachable yet.)");
        warmup.Attempts.Should().Be(2);
        (await _CheckAsync(host.Services)).Status.Should().Be(HealthStatus.Healthy);
        await host.StopAsync(AbortToken);
    }

    [Fact]
    public async Task should_be_ready_while_a_warmup_that_does_not_gate_readiness_still_runs()
    {
        // given
        var never = new TaskCompletionSource();
        using var host = _BuildHost(services =>
            services.AddWarmup(_ => new BlockingWarmup("Optional", never.Task) { Gates = false })
        );

        // when
        await host.StartAsync(AbortToken);
        await _Runner(host).WaitForInitializationAsync(AbortToken).WaitAsync(TimeSpan.FromSeconds(10), AbortToken);

        // then
        (await _CheckAsync(host.Services))
            .Status.Should()
            .Be(HealthStatus.Healthy);
        await host.StopAsync(AbortToken);
    }

    [Fact]
    public async Task should_cancel_a_running_warmup_and_stop_cleanly_when_the_host_stops()
    {
        // given
        var never = new TaskCompletionSource();
        var warmup = new BlockingWarmup("Endless", never.Task);
        using var host = _BuildHost(services => services.AddWarmup(_ => warmup));
        await host.StartAsync(AbortToken);
        await warmup.Started.WaitAsync(TimeSpan.FromSeconds(10), AbortToken);

        // when
        var act = () => host.StopAsync(AbortToken);

        // then
        await act.Should().CompleteWithinAsync(TimeSpan.FromSeconds(10));
        warmup.Canceled.Should().BeTrue();
    }

    [Fact]
    public void should_register_the_runner_and_one_ready_check_tagged_host_state_however_often_warmups_are_added()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.AddWarmup<CountingWarmup>();
        services.AddWarmup<CountingWarmup>();
        services.AddWarmup(_ => new BlockingWarmup("Other", Task.CompletedTask));
        using var provider = services.BuildServiceProvider();

        // then
        services.Count(d => d.ServiceType == typeof(IWarmup)).Should().Be(2);
        var registration = provider
            .GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value.Registrations.Should()
            .ContainSingle(r => r.Name == HeadlessWarmupServiceCollectionExtensions.HealthCheckName)
            .Which;
        registration.Tags.Should().Contain([HeadlessHealthCheckTags.Ready, HeadlessHealthCheckTags.HostState]);
    }

    private static IHost _BuildHost(Action<IServiceCollection> configure)
    {
        var builder = Host.CreateApplicationBuilder();
        configure(builder.Services);

        return builder.Build();
    }

    private static bool _HasError(HealthReportEntry entry) =>
        entry.Exception?.Message.Contains("last error", StringComparison.Ordinal) == true;

    private static WarmupRunner _Runner(IHost host) => host.Services.GetRequiredService<WarmupRunner>();

    private static async Task<HealthReportEntry> _CheckAsync(IServiceProvider services)
    {
        var report = await services
            .GetRequiredService<HealthCheckService>()
            .CheckHealthAsync(
                r =>
                    string.Equals(
                        r.Name,
                        HeadlessWarmupServiceCollectionExtensions.HealthCheckName,
                        StringComparison.Ordinal
                    ),
                AbortToken
            );

        return report.Entries[HeadlessWarmupServiceCollectionExtensions.HealthCheckName];
    }

    private sealed class BlockingWarmup(string name, Task release) : IWarmup
    {
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string Name => name;

        public bool Gates { get; init; } = true;

        public bool GatesReadiness => Gates;

        public Task Started => _started.Task;

        public bool Canceled { get; private set; }

        public async Task WarmUpAsync(CancellationToken cancellationToken)
        {
            _started.TrySetResult();

            try
            {
                await release.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                Canceled = true;

                throw;
            }
        }
    }

    private sealed class FlakyWarmup(string name, int failures) : IWarmup
    {
        private readonly TaskCompletionSource _firstFailure = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string Name => name;

        public int Attempts { get; private set; }

        public Task FirstFailure => _firstFailure.Task;

        public Task WarmUpAsync(CancellationToken cancellationToken)
        {
            Attempts++;

            if (Attempts > failures)
            {
                return Task.CompletedTask;
            }

            _firstFailure.TrySetResult();

            return Task.FromException(new InvalidOperationException("The store is not reachable yet."));
        }
    }

    private sealed class CountingWarmup : IWarmup
    {
        public string Name => "Counting";

        public Task WarmUpAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
