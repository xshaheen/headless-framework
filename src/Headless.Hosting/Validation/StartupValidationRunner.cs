// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.ExceptionServices;
using Microsoft.Extensions.Hosting;

namespace Headless.Hosting.Validation;

/// <summary>Runs every registered <see cref="IHeadlessStartupValidator" /> before any hosted service starts.</summary>
/// <remarks>
/// Registered as an <see cref="IHostedService" /> the first time a validator is added, so its
/// <see cref="StartingAsync" /> runs at that registration's position among the lifecycle services and before every
/// <see cref="IHostedService.StartAsync" />. Every validator runs even after one fails, so the host reports all
/// misconfigurations at once.
/// </remarks>
internal sealed class StartupValidationRunner(IEnumerable<IHeadlessStartupValidator> validators)
    : IHostedLifecycleService
{
    /// <inheritdoc />
    /// <exception cref="StartupValidationException">Two or more validators failed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken" /> was cancelled.</exception>
    /// <remarks>A single failing validator's exception is rethrown unwrapped, with its original stack trace.</remarks>
    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        List<Exception>? failures = null;

        foreach (var validator in validators)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await validator.ValidateAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
#pragma warning disable CA1031 // Collecting, not swallowing: every failure is rethrown below, and running the rest first lets one start report every misconfiguration.
            catch (Exception exception)
#pragma warning restore CA1031
            {
                (failures ??= []).Add(exception);
            }
        }

        if (failures is null)
        {
            return;
        }

        if (failures.Count == 1)
        {
            ExceptionDispatchInfo.Throw(failures[0]);
        }

        var lines = failures.Select(failure => $"  - {failure.Message}");

        throw new StartupValidationException(
            $"Headless startup validation failed: {failures.Count} checks reported problems."
                + Environment.NewLine
                + string.Join(Environment.NewLine, lines),
            failures
        );
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
