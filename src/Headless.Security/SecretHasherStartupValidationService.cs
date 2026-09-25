// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Headless.Security;

/// <summary>
/// Checks the secret-hasher configuration when the host starts: the selected algorithm must be registered, and its
/// measured hash cost must fall inside <see cref="SecretHasherCostCheckOptions" />.
/// </summary>
/// <remarks>
/// <para>
/// The setup builder already guarantees exactly one selected algorithm; the registration check catches a <c>Use*</c>
/// extension that selected an id without registering a matching <see cref="ISecretHashAlgorithm" />. It is not
/// governed by <see cref="SecretHasherCostCheckOptions.Mode" />: a hasher that cannot hash is always a startup failure,
/// never a warning. It runs in <see cref="StartingAsync" />, before any other hosted service starts.
/// </para>
/// <para>
/// The benchmark hashes once to warm up, then three more times, and judges the median so one scheduler hiccup cannot
/// trip it. It measures with the registered <see cref="TimeProvider" />, once per service instance, because each
/// instance verifies on its own hardware. Where it runs depends on the mode. <c>Strict</c> asks for a startup gate, so
/// it runs in <see cref="StartingAsync" /> and blocks the host. <c>Warn</c> only logs, so it runs in the background
/// from <see cref="StartedAsync" /> and never delays startup; stopping the host cancels it between samples.
/// </para>
/// </remarks>
internal sealed class SecretHasherStartupValidationService(
    IServiceProvider serviceProvider,
    IOptions<SecretHasherOptions> options
) : IHostedLifecycleService, IDisposable
{
    private const int _MeasuredSamples = 3;

    // A fixed, obviously synthetic secret: the benchmark measures cost, so its value is irrelevant.
    private static readonly byte[] _BenchmarkSecret = "headless-secret-hasher-cost-check"u8.ToArray();

    private readonly CancellationTokenSource _stopping = new();

    private int _hasRun;
    private ISecretHashAlgorithm? _deferredAlgorithm;
    private Task? _backgroundCheck;

    /// <summary>Gets the background <c>Warn</c> benchmark, or a completed task when none was started.</summary>
    internal Task BackgroundCheck => _backgroundCheck ?? Task.CompletedTask;

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">
    /// The selected algorithm is not registered, or the measured cost is out of range and
    /// <see cref="SecretHasherCostCheckOptions.Mode" /> is <see cref="SecretHasherCostCheckMode.Strict" />.
    /// </exception>
    public Task StartingAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _hasRun, 1) == 1)
        {
            return Task.CompletedTask;
        }

        var costCheck = options.Value.CostCheck;
        var selected = serviceProvider.GetRequiredService<SecretHasherAlgorithmSelection>().AlgorithmId;
        var algorithm =
            serviceProvider
                .GetServices<ISecretHashAlgorithm>()
                .FirstOrDefault(a => string.Equals(a.Id, selected, StringComparison.Ordinal))
            ?? throw new InvalidOperationException(SecretHasherErrors.AlgorithmNotRegistered(selected));

        if (costCheck.Mode is SecretHasherCostCheckMode.Warn)
        {
            _deferredAlgorithm = algorithm;
        }
        else if (
            costCheck.Mode is SecretHasherCostCheckMode.Strict
            && _MeasureCost(algorithm, costCheck, cancellationToken) is { } problem
        )
        {
            throw new InvalidOperationException(problem);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StartedAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _deferredAlgorithm, null) is { } algorithm)
        {
            var costCheck = options.Value.CostCheck;
            _backgroundCheck = Task.Run(
                () => _WarnOnCost(algorithm, costCheck, _stopping.Token),
                CancellationToken.None
            );
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StoppingAsync(CancellationToken cancellationToken)
    {
        if (_backgroundCheck is not { } backgroundCheck)
        {
            return;
        }

        await _stopping.CancelAsync().ConfigureAwait(false);

        // _WarnOnCost never faults, so this only waits for the in-flight hash to finish.
        await backgroundCheck.WaitAsync(cancellationToken).ConfigureAwait(false);
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

    /// <inheritdoc />
    public void Dispose()
    {
        _stopping.Dispose();
    }

    private void _WarnOnCost(
        ISecretHashAlgorithm algorithm,
        SecretHasherCostCheckOptions costCheck,
        CancellationToken cancellationToken
    )
    {
        var logger = _CreateLogger();

        try
        {
            if (_MeasureCost(algorithm, costCheck, cancellationToken) is { } problem)
            {
                logger.LogSecretHasherCostOutOfRange(problem);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The host is stopping; an unfinished benchmark has nothing to report.
        }
#pragma warning disable CA1031 // Nothing awaits this advisory background check, so a failed hash is logged instead of becoming an unobserved task exception.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            logger.LogSecretHasherCostCheckFailed(exception, algorithm.Id);
        }
    }

    private string? _MeasureCost(
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
            // Each hash can take up to MaximumDuration, so a cancelled startup or shutdown stops between samples.
            cancellationToken.ThrowIfCancellationRequested();
            var start = timeProvider.GetTimestamp();
            algorithm.Hash(_BenchmarkSecret);
            samples[i] = timeProvider.GetElapsedTime(start);
        }

        Array.Sort(samples);
        var median = samples[samples.Length / 2];
        var measured = _Milliseconds(median);

        if (median < costCheck.MinimumDuration)
        {
            return $"Secret hashing with '{algorithm.Id}' took {measured} ms, faster than the configured minimum of "
                + $"{_Milliseconds(costCheck.MinimumDuration)} ms. The cost parameters are probably test-grade; raise "
                + "them so an offline attacker pays a real price per guess.";
        }

        if (median > costCheck.MaximumDuration)
        {
            return $"Secret hashing with '{algorithm.Id}' took {measured} ms, slower than the configured maximum of "
                + $"{_Milliseconds(costCheck.MaximumDuration)} ms. Every sign-in will wait this long and concurrent "
                + "verifications can exhaust CPU; lower the cost parameters.";
        }

        return null;
    }

    private ILogger _CreateLogger()
    {
        return serviceProvider.GetService<ILoggerFactory>()?.CreateLogger(typeof(SecretHasherStartupValidationService))
            ?? NullLogger.Instance;
    }

    private static string _Milliseconds(TimeSpan duration)
    {
        return duration.TotalMilliseconds.ToString("0.##", CultureInfo.InvariantCulture);
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

    [LoggerMessage(
        EventId = 2,
        EventName = "SecretHasherCostCheckFailed",
        Level = LogLevel.Warning,
        Message = "The secret-hasher cost check could not hash with '{AlgorithmId}'; its cost was not measured."
    )]
    public static partial void LogSecretHasherCostCheckFailed(
        this ILogger logger,
        Exception exception,
        string algorithmId
    );
}
