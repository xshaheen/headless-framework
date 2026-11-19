// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;
using Headless.Testing;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests.Telemetry;

// The start and ready measurements carry no tag that tells one host from another, so no other host may start while
// these tests record.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class HostingTelemetryCollection
{
    public const string Name = "Hosting telemetry";
}

[Collection(HostingTelemetryCollection.Name)]
public sealed class HostingTelemetryTests : TestBase
{
    private const string _ValidatorDuration = "headless.hosting.startup_validator.duration";
    private const string _WarmupDuration = "headless.hosting.warmup.duration";
    private const string _StartDuration = "headless.hosting.start.duration";
    private const string _ReadyDuration = "headless.hosting.ready.duration";

    [Fact]
    public async Task should_record_each_startup_validator_with_its_outcome_and_the_error_type_of_a_failure()
    {
        // given
        var services = new ServiceCollection();
        services.AddStartupValidator<PassingValidator>();
        services.AddStartupValidator<FailingValidator>();
        await using var provider = services.BuildServiceProvider();
        using var recorder = new TelemetryRecorder(HostingDiagnostics.MeterName);

        // when
        var act = () =>
            provider.GetServices<IHostedService>().OfType<IHostedLifecycleService>().Single().StartingAsync(AbortToken);

        // then
        await act.Should().ThrowExactlyAsync<ValidatorFailedException>();
        var measurements = recorder.Of(_ValidatorDuration);
        measurements.Should().HaveCount(2).And.OnlyContain(m => m.Unit == "ms" && m.Value >= 0);
        measurements
            .Select(m => m.Tags)
            .Should()
            .BeEquivalentTo([
                _Tags(
                    ("headless.hosting.startup_validator.name", typeof(PassingValidator).FullName),
                    ("headless.hosting.startup_validator.outcome", "success")
                ),
                _Tags(
                    ("headless.hosting.startup_validator.name", typeof(FailingValidator).FullName),
                    ("headless.hosting.startup_validator.outcome", "failure"),
                    ("error.type", typeof(ValidatorFailedException).FullName)
                ),
            ]);
    }

    [Fact]
    public async Task should_name_a_generic_validator_by_its_generic_definition()
    {
        // given
        var services = new ServiceCollection();
        services.AddStartupValidator<GenericValidator<PassingValidator>>();
        await using var provider = services.BuildServiceProvider();
        using var recorder = new TelemetryRecorder(HostingDiagnostics.MeterName);

        // when
        await provider
            .GetServices<IHostedService>()
            .OfType<IHostedLifecycleService>()
            .Single()
            .StartingAsync(AbortToken);

        // then
        recorder
            .Of(_ValidatorDuration)
            .Should()
            .ContainSingle()
            .Which.Tags["headless.hosting.startup_validator.name"]
            .Should()
            .Be(typeof(GenericValidator<>).FullName);
    }

    [Fact]
    public async Task should_record_each_warmup_attempt_with_its_number_and_outcome()
    {
        // given
        using var recorder = new TelemetryRecorder(HostingDiagnostics.MeterName);
        using var host = _BuildHost(services => services.AddWarmup(_ => new FlakyWarmup("Store-retry", failures: 1)));

        // when
        await host.StartAsync(AbortToken);
        await host.Services.WaitForWarmupAsync(AbortToken).Bounded();

        // then
        var attempts = recorder
            .Of(_WarmupDuration)
            .Where(m => Equals(m.Tags["headless.hosting.warmup.name"], "Store-retry"))
            .ToList();
        attempts.Should().HaveCount(2).And.OnlyContain(m => m.Unit == "ms");
        attempts[0]
            .Tags.Should()
            .BeEquivalentTo(
                _Tags(
                    ("headless.hosting.warmup.name", "Store-retry"),
                    ("headless.hosting.warmup.attempt", 1),
                    ("headless.hosting.warmup.outcome", "failure"),
                    ("error.type", typeof(InvalidOperationException).FullName)
                )
            );
        attempts[1]
            .Tags.Should()
            .BeEquivalentTo(
                _Tags(
                    ("headless.hosting.warmup.name", "Store-retry"),
                    ("headless.hosting.warmup.attempt", 2),
                    ("headless.hosting.warmup.outcome", "success")
                )
            );
        await host.StopAsync(AbortToken);
    }

    [Fact]
    public void should_cap_the_attempt_tag_so_a_warmup_that_keeps_retrying_adds_no_new_series()
    {
        // given
        using var recorder = new TelemetryRecorder(HostingDiagnostics.MeterName);

        // when
        HostingTelemetry.WarmupAttemptCompleted("Capped", attempt: 25, TimeSpan.FromMilliseconds(5), fault: null);

        // then
        var measurement = recorder.Of(_WarmupDuration).Should().ContainSingle().Which;
        measurement.Tags["headless.hosting.warmup.attempt"].Should().Be(10);
        measurement.Value.Should().Be(5);
    }

    [Fact]
    public async Task should_record_start_once_when_the_host_starts_and_ready_once_when_the_last_gating_warmup_finishes()
    {
        // given
        var cache = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var recorder = new TelemetryRecorder(HostingDiagnostics.MeterName);
        using var host = _BuildHost(services =>
        {
            services.AddWarmup(_ => new BlockingWarmup("Cache", cache.Task));
            services.AddWarmup(_ => new BlockingWarmup("Store", store.Task));
        });

        // when
        await host.StartAsync(AbortToken);

        // then
        recorder.Of(_StartDuration).Should().ContainSingle().Which.Value.Should().BePositive();
        recorder.Of(_ReadyDuration).Should().BeEmpty();

        cache.SetResult();
        store.SetResult();
        await host.Services.WaitForWarmupAsync(AbortToken).Bounded();

        var ready = recorder.Of(_ReadyDuration).Should().ContainSingle().Which;
        ready.Unit.Should().Be("ms");
        ready.Value.Should().BePositive();
        ready.Tags.Should().BeEmpty();
        recorder.Of(_StartDuration).Should().ContainSingle();
        await host.StopAsync(AbortToken);
    }

    private static IHost _BuildHost(Action<IServiceCollection> configure)
    {
        var builder = Host.CreateApplicationBuilder();
        configure(builder.Services);

        return builder.Build();
    }

    private static Dictionary<string, object?> _Tags(params (string Key, object? Value)[] tags)
    {
        return tags.ToDictionary(t => t.Key, t => t.Value, StringComparer.Ordinal);
    }

    private sealed class ValidatorFailedException(string message) : InvalidOperationException(message);

    private sealed class PassingValidator : IHeadlessStartupValidator
    {
        public Task ValidateAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FailingValidator : IHeadlessStartupValidator
    {
        public Task ValidateAsync(CancellationToken cancellationToken) =>
            Task.FromException(new ValidatorFailedException("The connection string is missing."));
    }

    private sealed class GenericValidator<T> : IHeadlessStartupValidator
    {
        public Task ValidateAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class BlockingWarmup(string name, Task release) : IWarmup
    {
        public string Name => name;

        public Task WarmUpAsync(CancellationToken cancellationToken) => release.WaitAsync(cancellationToken);
    }

    private sealed class FlakyWarmup(string name, int failures) : IWarmup
    {
        private int _attempts;

        public string Name => name;

        public Task WarmUpAsync(CancellationToken cancellationToken) =>
            ++_attempts > failures
                ? Task.CompletedTask
                : Task.FromException(new InvalidOperationException("The store is not reachable yet."));
    }
}
