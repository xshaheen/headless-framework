// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Headless.Security;

/// <summary>
/// Checks the secret-hasher configuration when the host starts: the configured algorithm must be registered, and its
/// measured hash cost must fall inside <see cref="SecretHasherCostCheckOptions" />.
/// </summary>
/// <remarks>
/// <para>
/// The registration check lives here rather than in the options validator because validators run while options are
/// built, and resolving the algorithms from there would re-enter that construction (algorithms read options). It is
/// not governed by <see cref="SecretHasherCostCheckOptions.Mode" />: a hasher that cannot hash is always a startup
/// failure, never a warning.
/// </para>
/// <para>
/// The benchmark hashes once to warm up, then three more times, and judges the median so one scheduler hiccup cannot
/// trip it. It measures with the registered <see cref="TimeProvider" />. It runs in <see cref="StartingAsync" /> so a
/// misconfiguration stops the host before any other hosted service starts, and it runs once per service instance.
/// </para>
/// </remarks>
internal sealed class SecretHasherStartupValidationService(
    IServiceProvider serviceProvider,
    IOptions<SecretHasherOptions> options
) : IHostedLifecycleService
{
    private const int _MeasuredSamples = 3;

    // A fixed, obviously synthetic secret: the benchmark measures cost, so its value is irrelevant.
    private static readonly byte[] _BenchmarkSecret = "headless-secret-hasher-cost-check"u8.ToArray();

    private int _hasRun;

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">
    /// The configured algorithm is not registered, or the measured cost is out of range and
    /// <see cref="SecretHasherCostCheckOptions.Mode" /> is <see cref="SecretHasherCostCheckMode.Strict" />.
    /// </exception>
    public Task StartingAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _hasRun, 1) == 1)
        {
            return Task.CompletedTask;
        }

        var current = options.Value;
        var algorithm =
            serviceProvider
                .GetServices<ISecretHashAlgorithm>()
                .FirstOrDefault(a => string.Equals(a.Id, current.Algorithm, StringComparison.Ordinal))
            ?? throw new InvalidOperationException(SecretHasherErrors.AlgorithmNotRegistered(current.Algorithm));

        if (current.CostCheck.Mode is SecretHasherCostCheckMode.Off)
        {
            return Task.CompletedTask;
        }

        _CheckCost(algorithm, current.CostCheck, cancellationToken);

        return Task.CompletedTask;
    }

    private void _CheckCost(
        ISecretHashAlgorithm algorithm,
        SecretHasherCostCheckOptions costCheck,
        CancellationToken cancellationToken
    )
    {
        var timeProvider = serviceProvider.GetService<TimeProvider>() ?? TimeProvider.System;

        // Warm-up: the first call pays JIT and allocator start-up costs that later calls never see.
        cancellationToken.ThrowIfCancellationRequested();
        algorithm.Hash(_BenchmarkSecret);

        var samples = new TimeSpan[_MeasuredSamples];

        for (var i = 0; i < samples.Length; i++)
        {
            // Each hash can take up to MaximumDuration, so a cancelled startup stops between samples.
            cancellationToken.ThrowIfCancellationRequested();
            var start = timeProvider.GetTimestamp();
            algorithm.Hash(_BenchmarkSecret);
            samples[i] = timeProvider.GetElapsedTime(start);
        }

        Array.Sort(samples);
        var median = samples[samples.Length / 2];

        string? problem = null;

        var measured = _Milliseconds(median);

        if (median < costCheck.MinimumDuration)
        {
            problem =
                $"Secret hashing with '{algorithm.Id}' took {measured} ms, faster than the configured minimum of "
                + $"{_Milliseconds(costCheck.MinimumDuration)} ms. The cost parameters are probably test-grade; raise "
                + "them so an offline attacker pays a real price per guess.";
        }
        else if (median > costCheck.MaximumDuration)
        {
            problem =
                $"Secret hashing with '{algorithm.Id}' took {measured} ms, slower than the configured maximum of "
                + $"{_Milliseconds(costCheck.MaximumDuration)} ms. Every sign-in will wait this long and concurrent "
                + "verifications can exhaust CPU; lower the cost parameters.";
        }

        if (problem is null)
        {
            return;
        }

        if (costCheck.Mode is SecretHasherCostCheckMode.Strict)
        {
            throw new InvalidOperationException(problem);
        }

        var logger =
            serviceProvider.GetService<ILoggerFactory>()?.CreateLogger(typeof(SecretHasherStartupValidationService))
            ?? NullLogger.Instance;

        logger.LogSecretHasherCostOutOfRange(problem);
    }

    private static string _Milliseconds(TimeSpan duration)
    {
        return duration.TotalMilliseconds.ToString("0.##", CultureInfo.InvariantCulture);
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StartedAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StoppingAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StoppedAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}

internal static partial class SecretHasherLoggerExtensions
{
    [LoggerMessage(
        EventId = 1,
        EventName = "SecretHasherCostOutOfRange",
        Level = LogLevel.Warning,
        Message = "{Problem} Set SecretHasherOptions.CostCheck.Mode to Strict to fail startup instead, or Off to skip the check."
    )]
    public static partial void LogSecretHasherCostOutOfRange(this ILogger logger, string problem);
}
