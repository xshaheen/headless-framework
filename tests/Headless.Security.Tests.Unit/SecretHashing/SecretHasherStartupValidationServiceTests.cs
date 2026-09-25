// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Tests.SecretHashing;

public sealed class SecretHasherStartupValidationServiceTests
{
    [Fact]
    public async Task should_fail_startup_naming_the_argon2_package_when_argon2id_is_configured_but_not_registered()
    {
        // given
        await using var provider = _BuildProvider(
            o =>
            {
                o.Algorithm = SecretHashAlgorithms.Argon2id;
                o.CostCheck.Mode = SecretHasherCostCheckMode.Off;
            },
            algorithm: null
        );

        // when
        var act = () => _Service(provider).StartingAsync(CancellationToken.None);

        // then
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Headless.Security.Argon2*");
    }

    [Fact]
    public async Task should_not_hash_when_cost_check_is_off()
    {
        // given
        var algorithm = new TimedAlgorithm();
        await using var provider = _BuildProvider(o => o.CostCheck.Mode = SecretHasherCostCheckMode.Off, algorithm);

        // when
        await _Service(provider).StartingAsync(CancellationToken.None);

        // then
        algorithm.HashCalls.Should().Be(0);
    }

    [Fact]
    public async Task should_warn_and_continue_when_hashing_is_faster_than_the_minimum()
    {
        // given
        var algorithm = new TimedAlgorithm { Cost = TimeSpan.FromMilliseconds(1) };
        var logger = new RecordingLoggerProvider();
        await using var provider = _BuildProvider(
            o => o.CostCheck.Mode = SecretHasherCostCheckMode.Warn,
            algorithm,
            logger
        );

        // when
        await _Service(provider).StartingAsync(CancellationToken.None);

        // then
        logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning && e.Message.Contains("faster"));
    }

    [Fact]
    public async Task should_fail_startup_when_hashing_is_faster_than_the_minimum_in_strict_mode()
    {
        // given
        var algorithm = new TimedAlgorithm { Cost = TimeSpan.FromMilliseconds(1) };
        await using var provider = _BuildProvider(o => o.CostCheck.Mode = SecretHasherCostCheckMode.Strict, algorithm);

        // when
        var act = () => _Service(provider).StartingAsync(CancellationToken.None);

        // then
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*faster*");
    }

    [Theory]
    [InlineData(SecretHasherCostCheckMode.Warn)]
    [InlineData(SecretHasherCostCheckMode.Strict)]
    public async Task should_report_hashing_slower_than_the_maximum(SecretHasherCostCheckMode mode)
    {
        // given
        var algorithm = new TimedAlgorithm { Cost = TimeSpan.FromSeconds(2) };
        var logger = new RecordingLoggerProvider();
        await using var provider = _BuildProvider(o => o.CostCheck.Mode = mode, algorithm, logger);

        // when
        var act = () => _Service(provider).StartingAsync(CancellationToken.None);

        // then
        if (mode == SecretHasherCostCheckMode.Strict)
        {
            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*slower*");
        }
        else
        {
            await act.Should().NotThrowAsync();
            logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning && e.Message.Contains("slower"));
        }
    }

    [Fact]
    public async Task should_log_nothing_when_duration_is_in_range()
    {
        // given
        var algorithm = new TimedAlgorithm { Cost = TimeSpan.FromMilliseconds(50) };
        var logger = new RecordingLoggerProvider();
        await using var provider = _BuildProvider(
            o => o.CostCheck.Mode = SecretHasherCostCheckMode.Strict,
            algorithm,
            logger
        );

        // when
        await _Service(provider).StartingAsync(CancellationToken.None);

        // then
        logger.Entries.Should().NotContain(e => e.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task should_stop_the_benchmark_between_samples_when_startup_is_cancelled()
    {
        // given — startup is cancelled while the warm-up hash is running.
        using var startup = new CancellationTokenSource();
        var algorithm = new TimedAlgorithm { AfterHash = startup.Cancel };
        await using var provider = _BuildProvider(o => o.CostCheck.Mode = SecretHasherCostCheckMode.Warn, algorithm);

        // when
        var act = () => _Service(provider).StartingAsync(startup.Token);

        // then
        await act.Should().ThrowAsync<OperationCanceledException>();
        algorithm.HashCalls.Should().Be(1);
    }

    [Fact]
    public async Task should_run_one_warm_up_and_three_measured_hashes_once()
    {
        // given
        var algorithm = new TimedAlgorithm { Cost = TimeSpan.FromMilliseconds(50) };
        await using var provider = _BuildProvider(o => o.CostCheck.Mode = SecretHasherCostCheckMode.Warn, algorithm);
        var sut = _Service(provider);

        // when
        await sut.StartingAsync(CancellationToken.None);
        await sut.StartingAsync(CancellationToken.None);

        // then
        algorithm.HashCalls.Should().Be(4);
    }

    [Fact]
    public async Task should_measure_the_median_so_one_slow_sample_does_not_trip_the_check()
    {
        // given — warm-up, then 50 ms, 3 s, 50 ms: the median stays in range.
        var algorithm = new TimedAlgorithm
        {
            Costs =
            [
                TimeSpan.FromSeconds(5),
                TimeSpan.FromMilliseconds(50),
                TimeSpan.FromSeconds(3),
                TimeSpan.FromMilliseconds(50),
            ],
        };
        await using var provider = _BuildProvider(o => o.CostCheck.Mode = SecretHasherCostCheckMode.Strict, algorithm);

        // when
        var act = () => _Service(provider).StartingAsync(CancellationToken.None);

        // then
        await act.Should().NotThrowAsync();
    }

    private static ServiceProvider _BuildProvider(
        Action<SecretHasherOptions> configure,
        TimedAlgorithm? algorithm,
        RecordingLoggerProvider? logger = null
    )
    {
        var services = new ServiceCollection();
        var time = new FakeTimeProvider();
        services.AddSingleton<TimeProvider>(time);
        services.AddLogging(builder => builder.AddProvider(logger ?? new RecordingLoggerProvider()));
        services
            .AddOptions<SecretHasherOptions>()
            .Configure(o =>
            {
                o.Algorithm = "timed";
                configure(o);
            });

        if (algorithm is not null)
        {
            algorithm.Time = time;
            services.AddSingleton<ISecretHashAlgorithm>(algorithm);
        }

        return services.BuildServiceProvider();
    }

    private static SecretHasherStartupValidationService _Service(IServiceProvider provider)
    {
        return new SecretHasherStartupValidationService(
            provider,
            provider.GetRequiredService<IOptions<SecretHasherOptions>>()
        );
    }

    /// <summary>An algorithm whose every hash advances a fake clock by a scripted cost.</summary>
    private sealed class TimedAlgorithm : ISecretHashAlgorithm
    {
        public FakeTimeProvider? Time { get; set; }

        public TimeSpan Cost { get; init; } = TimeSpan.FromMilliseconds(50);

        public TimeSpan[]? Costs { get; init; }

        public int HashCalls { get; private set; }

        public Action? AfterHash { get; init; }

        public string Id => "timed";

        public string Hash(ReadOnlySpan<byte> secret)
        {
            var cost = Costs is { } costs ? costs[HashCalls] : Cost;
            HashCalls++;
            AfterHash?.Invoke();
            Time!.Advance(cost);

            return new PhcString(Id, null, [], new byte[16], new byte[16]).ToString();
        }

        public bool TryComputeHash(ReadOnlySpan<byte> secret, PhcString encoded, Span<byte> destination)
        {
            return false;
        }

        public bool NeedsRehash(PhcString encoded)
        {
            return false;
        }
    }
}
