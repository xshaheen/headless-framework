// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.MultiTenancy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Tests;

public sealed class HeadlessTenancyStartupValidatorTests
{
    [Fact]
    public async Task should_complete_startup_when_validators_return_only_warning_and_information_diagnostics()
    {
        // Only Error severity blocks startup; Warning/Information must be logged and let the host start.
        var logger = new CapturingLogger();
        await using var provider = new ServiceCollection().BuildServiceProvider();
        var validator = new HeadlessTenancyStartupValidator(
            [new WarnInfoValidator()],
            provider,
            new TenantPostureManifest(),
            logger
        );

        var act = () => validator.StartingAsync(TestContext.Current.CancellationToken);

        await act.Should().NotThrowAsync();
        logger.Entries.Should().ContainSingle(entry => entry.Level == LogLevel.Warning);
        logger.Entries.Should().ContainSingle(entry => entry.Level == LogLevel.Information);
        logger.Entries.Should().NotContain(entry => entry.Level == LogLevel.Error);
    }

    [Fact]
    public async Task should_stop_running_remaining_validators_when_token_cancelled_mid_run()
    {
        // The per-iteration ThrowIfCancellationRequested must honor host shutdown between validators,
        // without converting the cancellation into a synthetic VALIDATOR_THREW diagnostic.
        using var cts = new CancellationTokenSource();
        var second = new CountingValidator();
        var logger = new CapturingLogger();
        await using var provider = new ServiceCollection().BuildServiceProvider();
        var validator = new HeadlessTenancyStartupValidator(
            [new CancelSourceValidator(cts), second],
            provider,
            new TenantPostureManifest(),
            logger
        );

        var act = () => validator.StartingAsync(cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        second.Calls.Should().Be(0);
        logger.Entries.Should().NotContain(entry => entry.Level == LogLevel.Error);
    }

    [Fact]
    public async Task should_hand_validators_the_same_manifest_instance_that_setup_recorded()
    {
        // Guards the split-manifest hazard documented on GetOrAddTenantPostureManifest: the manifest
        // written by AddHeadlessTenancy seam recording must be the instance validators receive and
        // the instance resolved from DI.
        var builder = Host.CreateApplicationBuilder();
        var capturing = new ManifestCapturingValidator();
        builder.Services.AddSingleton<IHeadlessTenancyValidator>(capturing);
        builder.AddHeadlessTenancy(tenancy =>
            tenancy.RecordSeam("Http", TenantPostureStatus.Enforcing, "require-tenant")
        );

        await using var provider = builder.Services.BuildServiceProvider();
        var hostedService = (IHostedLifecycleService)
            provider
                .GetServices<IHostedService>()
                .Single(service =>
                    string.Equals(service.GetType().Name, "HeadlessTenancyStartupValidator", StringComparison.Ordinal)
                );

        await hostedService.StartingAsync(TestContext.Current.CancellationToken);

        capturing.SeenManifest.Should().BeSameAs(provider.GetRequiredService<TenantPostureManifest>());
        var seam = capturing.SeenManifest!.GetSeam("Http");
        seam.Should().NotBeNull();
        seam!.Status.Should().Be(TenantPostureStatus.Enforcing);
        seam.Capabilities.Should().Equal("require-tenant");
    }

    private sealed class WarnInfoValidator : IHeadlessTenancyValidator
    {
        public IEnumerable<HeadlessTenancyDiagnostic> Validate(HeadlessTenancyValidationContext context)
        {
            yield return HeadlessTenancyDiagnostic.Warning("Http", "HEADLESS_WARN", "Non-blocking warning.");
            yield return HeadlessTenancyDiagnostic.Information("Http", "HEADLESS_INFO", "Non-blocking info.");
        }
    }

    private sealed class CancelSourceValidator(CancellationTokenSource cancellationTokenSource)
        : IHeadlessTenancyValidator
    {
        public IEnumerable<HeadlessTenancyDiagnostic> Validate(HeadlessTenancyValidationContext context)
        {
            cancellationTokenSource.Cancel();

            return [];
        }
    }

    private sealed class CountingValidator : IHeadlessTenancyValidator
    {
        public int Calls { get; private set; }

        public IEnumerable<HeadlessTenancyDiagnostic> Validate(HeadlessTenancyValidationContext context)
        {
            Calls++;

            return [];
        }
    }

    private sealed class ManifestCapturingValidator : IHeadlessTenancyValidator
    {
        public TenantPostureManifest? SeenManifest { get; private set; }

        public IEnumerable<HeadlessTenancyDiagnostic> Validate(HeadlessTenancyValidationContext context)
        {
            SeenManifest = context.Manifest;

            return [];
        }
    }

    private sealed class CapturingLogger : ILogger<HeadlessTenancyStartupValidator>
    {
        private readonly List<(LogLevel Level, string Message)> _entries = [];

        public IReadOnlyList<(LogLevel Level, string Message)> Entries
        {
            get
            {
                lock (_entries)
                {
                    return [.. _entries];
                }
            }
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            lock (_entries)
            {
                _entries.Add((logLevel, formatter(state, exception)));
            }
        }
    }
}
