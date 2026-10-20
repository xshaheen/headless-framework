// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Threading;
using Microsoft.Extensions.Logging;

namespace Headless.Hosting;

/// <summary>
/// A seeder that writes a feature's code-declared definitions into its definition store, retrying a transient failure
/// with jittered exponential back-off (from one second, capped at 30 seconds) up to <see cref="MaxRetries"/> times.
/// Cancellation, invalid arguments, and unsupported operations are not retried.
/// </summary>
/// <remarks>
/// Features, Permissions, and Settings each derive one, naming it and pointing it at their store. The retry waits real
/// time: pass <see cref="TimeProvider.System"/>, or nothing, so a host that fakes its app clock does not stall it.
/// </remarks>
/// <param name="logger">Logs each failed attempt.</param>
/// <param name="retryClock">The back-off's clock; <see langword="null"/> means <see cref="TimeProvider.System"/>.</param>
[PublicAPI]
public abstract partial class DefinitionStoreSeeder(ILogger logger, TimeProvider? retryClock = null) : ISeeder
{
    /// <summary>How many times a failed save is retried after the first attempt.</summary>
    public const int MaxRetries = 10;

    /// <inheritdoc />
    public abstract string Name { get; }

    /// <summary>Gets whether the application stores its static definitions, so there is anything to write.</summary>
    protected abstract bool IsEnabled { get; }

    /// <summary>Writes the code-declared definitions into the store.</summary>
    /// <param name="cancellationToken">Cancels the save.</param>
    /// <returns>A task that completes when the definitions are saved.</returns>
    protected abstract Task SaveAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Reports whether the store already holds the code-declared definitions. Default: <see cref="SeedState.Unknown" />;
    /// a feature overrides it to compare its store with its definitions without writing.
    /// </summary>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <returns>Whether the store is current.</returns>
    protected virtual Task<SeedState> VerifyStoreAsync(CancellationToken cancellationToken) =>
        Task.FromResult(SeedState.Unknown);

    /// <inheritdoc />
    public Task<SeedState> VerifyAsync(CancellationToken cancellationToken) =>
        // A store the application does not write to holds no seeded definitions to fall behind.
        IsEnabled ? VerifyStoreAsync(cancellationToken) : Task.FromResult(SeedState.Current);

    /// <inheritdoc />
    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        if (!IsEnabled)
        {
            return;
        }

        var backoff = new ReconnectBackoff(retryClock);

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await SaveAsync(cancellationToken).ConfigureAwait(false);

                return;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogSaveFailed(logger, exception, Name, attempt);

                if (exception is ArgumentException or NotSupportedException || attempt > MaxRetries)
                {
                    throw;
                }
            }

            if (!await backoff.WaitAsync(cancellationToken).ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
        }
    }

    [LoggerMessage(
        EventId = 1,
        EventName = "FailedToSaveStaticDefinitions",
        Level = LogLevel.Error,
        Message = "Seeder {Seeder} failed to save its static definitions to the database (attempt {Attempt})"
    )]
    private static partial void LogSaveFailed(ILogger logger, Exception exception, string seeder, int attempt);
}
