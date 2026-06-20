// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting.Initialization;
using Headless.Settings.Definitions;
using Headless.Settings.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;

namespace Headless.Settings.Seeders;

/// <summary>
/// Hosted service that seeds static setting definitions to the database and pre-caches dynamic settings
/// on application startup. Implements <see cref="IInitializer"/> so dependents can await
/// <see cref="WaitForInitializationAsync"/> before serving requests.
/// </summary>
public sealed class SettingsInitializationBackgroundService(
    TimeProvider timeProvider,
    IServiceScopeFactory serviceScopeFactory,
    IOptions<SettingManagementOptions> optionsAccessor,
    ILogger<SettingsInitializationBackgroundService> logger
) : IHostedService, IDisposable, IInitializer
{
    private readonly TaskCompletionSource _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly SettingManagementOptions _options = optionsAccessor.Value;
    private CancellationTokenSource? _linkedCts;
    private Task? _initializeDynamicSettingsTask;

    /// <summary>Gets a value indicating whether initialization has completed successfully.</summary>
    public bool IsInitialized => _tcs.Task.IsCompletedSuccessfully;

    /// <summary>Returns a task that completes when initialization finishes or the token is cancelled.</summary>
    /// <param name="cancellationToken">Token to cancel the wait.</param>
    /// <returns>A <see cref="Task"/> that completes when the service is initialized.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled before initialization completed.</exception>
    public Task WaitForInitializationAsync(CancellationToken cancellationToken = default) =>
        _tcs.Task.WaitAsync(cancellationToken);

    /// <inheritdoc/>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_options is { SaveStaticSettingsToDatabase: false, IsDynamicSettingStoreEnabled: false })
        {
            // Both features disabled — initialization is a no-op; signal completion immediately.
            _tcs.TrySetResult();

            return Task.CompletedTask;
        }

        _linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cancellationTokenSource.Token);
        _initializeDynamicSettingsTask = _InitializeDynamicSettingsAsync(_linkedCts.Token);

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _cancellationTokenSource.CancelAsync().ConfigureAwait(false);

        if (_initializeDynamicSettingsTask is not null)
        {
            try
            {
#pragma warning disable VSTHRD003 // IHostedService pattern: task started in StartAsync, awaited in StopAsync
                await _initializeDynamicSettingsTask.WaitAsync(cancellationToken).ConfigureAwait(false);
#pragma warning restore VSTHRD003
            }
            catch (OperationCanceledException) { }
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _linkedCts?.Dispose();
        _cancellationTokenSource.Dispose();
    }

    private async Task _InitializeDynamicSettingsAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            await using var scope = serviceScopeFactory.CreateAsyncScope();

            await _SaveStaticSettingsToDatabaseAsync(scope, cancellationToken).ConfigureAwait(false);

            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            await _PreCacheDynamicSettingsAsync(scope, cancellationToken).ConfigureAwait(false);

            _tcs.TrySetResult();
        }
        catch (OperationCanceledException)
        {
            _tcs.TrySetCanceled(cancellationToken);
        }
        catch (Exception ex)
        {
            _tcs.TrySetException(ex);
        }
    }

    private async Task _SaveStaticSettingsToDatabaseAsync(AsyncServiceScope scope, CancellationToken cancellationToken)
    {
        if (!_options.SaveStaticSettingsToDatabase)
        {
            return;
        }

        var options = new RetryStrategyOptions
        {
            Name = "SaveStaticSettingsToDatabaseRetry",
            Delay = TimeSpan.FromSeconds(2),
            MaxRetryAttempts = 10,
            BackoffType = DelayBackoffType.Exponential,
            UseJitter = false,
            ShouldHandle = new PredicateBuilder().Handle<Exception>(),
        };

        var builder = new ResiliencePipelineBuilder { TimeProvider = timeProvider };
        var pipeline = builder.AddRetry(options).Build();

        await pipeline.ExecuteAsync(
            static async (state, cancellationToken) =>
            {
                var (scope, logger) = state;

                var store = scope.ServiceProvider.GetRequiredService<IDynamicSettingDefinitionStore>();

                try
                {
                    await store.SaveAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    logger.LogFailedToSaveStaticSettings(e);

                    throw; // Polly will catch it
                }
            },
            (scope, logger),
            cancellationToken
        );
    }

    private async Task _PreCacheDynamicSettingsAsync(AsyncServiceScope scope, CancellationToken cancellationToken)
    {
        if (!_options.IsDynamicSettingStoreEnabled)
        {
            return;
        }

        var store = scope.ServiceProvider.GetRequiredService<IDynamicSettingDefinitionStore>();

        try
        {
            await store.GetAllAsync(cancellationToken).ConfigureAwait(false); // Pre-cache settings, so the first request doesn't wait
        }
        catch (Exception e)
        {
            logger.LogFailedToPreCacheDynamicSettings(e);

            throw;
        }
    }
}

/// <summary>Structured log helpers for <see cref="SettingsInitializationBackgroundService"/>.</summary>
internal static partial class SettingsInitializationBackgroundServiceLog
{
    [LoggerMessage(
        EventId = 1,
        EventName = "FailedToSaveStaticSettings",
        Level = LogLevel.Error,
        Message = "Failed to save static settings to the database"
    )]
    public static partial void LogFailedToSaveStaticSettings(this ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 2,
        EventName = "FailedToPreCacheDynamicSettings",
        Level = LogLevel.Error,
        Message = "Failed to pre-cache dynamic settings"
    )]
    public static partial void LogFailedToPreCacheDynamicSettings(this ILogger logger, Exception exception);
}
