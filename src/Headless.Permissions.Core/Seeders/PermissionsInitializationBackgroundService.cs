// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting.Initialization;
using Headless.Permissions.Definitions;
using Headless.Permissions.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;

namespace Headless.Permissions.Seeders;

/// <summary>
/// Hosted service that synchronizes static permission definitions to the dynamic store on application startup.
/// Implements <see cref="IInitializer"/> so other components can await readiness via
/// <see cref="WaitForInitializationAsync"/>. The service is a no-op when both
/// <c>SaveStaticPermissionsToDatabase</c> and <c>IsDynamicPermissionStoreEnabled</c> are <see langword="false"/>,
/// in which case <see cref="IsInitialized"/> is set to <see langword="true"/> immediately.
/// <para>
/// On failure the <see cref="IInitializer.WaitForInitializationAsync"/> task surfaces the exception so
/// dependents receive it rather than hanging indefinitely.
/// </para>
/// </summary>
public sealed class PermissionsInitializationBackgroundService(
    TimeProvider timeProvider,
    IServiceScopeFactory serviceScopeFactory,
    IOptions<PermissionManagementOptions> optionsAccessor,
    ILogger<PermissionsInitializationBackgroundService> logger
) : IHostedService, IDisposable, IInitializer
{
    private readonly TaskCompletionSource _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly PermissionManagementOptions _options = optionsAccessor.Value;
    private CancellationTokenSource? _linkedCts;
    private Task? _initializeDynamicPermissionsTask;

    /// <summary>
    /// <see langword="true"/> once the startup sync has completed successfully;
    /// <see langword="false"/> while it is still running or has failed.
    /// </summary>
    public bool IsInitialized => _tcs.Task.IsCompletedSuccessfully;

    /// <summary>
    /// Returns a task that completes when initialization finishes, or faults if it failed.
    /// Respects <paramref name="cancellationToken"/> so callers do not block indefinitely.
    /// </summary>
    public Task WaitForInitializationAsync(CancellationToken cancellationToken = default)
    {
        return _tcs.Task.WaitAsync(cancellationToken);
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_options is { SaveStaticPermissionsToDatabase: false, IsDynamicPermissionStoreEnabled: false })
        {
            // Both features disabled — initialization is a no-op; signal completion immediately.
            _tcs.TrySetResult();

            return Task.CompletedTask;
        }

        _linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cancellationTokenSource.Token);
        _initializeDynamicPermissionsTask = _InitializeDynamicPermissionsAsync(_linkedCts.Token);

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _cancellationTokenSource.CancelAsync().ConfigureAwait(false);

        if (_initializeDynamicPermissionsTask is not null)
        {
            try
            {
#pragma warning disable VSTHRD003 // IHostedService pattern: task started in StartAsync, awaited in StopAsync
                await _initializeDynamicPermissionsTask.WaitAsync(cancellationToken).ConfigureAwait(false);
#pragma warning restore VSTHRD003
            }
            catch (OperationCanceledException) { }
        }
    }

    public void Dispose()
    {
        _linkedCts?.Dispose();
        _cancellationTokenSource.Dispose();
    }

    private async Task _InitializeDynamicPermissionsAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            await using var scope = serviceScopeFactory.CreateAsyncScope();

            await _SaveStaticPermissionsToDatabaseAsync(scope, cancellationToken).ConfigureAwait(false);

            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            await _PreCacheDynamicPermissionsAsync(scope, cancellationToken).ConfigureAwait(false);

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

    private async Task _SaveStaticPermissionsToDatabaseAsync(
        AsyncServiceScope scope,
        CancellationToken cancellationToken
    )
    {
        if (!_options.SaveStaticPermissionsToDatabase)
        {
            return;
        }

        var options = new RetryStrategyOptions
        {
            Name = "SaveStaticPermissionToDatabaseRetry",
            Delay = TimeSpan.FromSeconds(2),
            MaxRetryAttempts = 10,
            BackoffType = DelayBackoffType.Exponential,
            UseJitter = false,
            ShouldHandle = new PredicateBuilder().Handle<Exception>(),
        };

        var builder = new ResiliencePipelineBuilder { TimeProvider = timeProvider };
        var pipeline = builder.AddRetry(options).Build();

        await pipeline
            .ExecuteAsync(
                static async (state, cancellationToken) =>
                {
                    var (scope, logger) = state;

                    var store = scope.ServiceProvider.GetRequiredService<IDynamicPermissionDefinitionStore>();

                    try
                    {
                        await store.SaveAsync(cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception e)
                    {
                        logger.LogFailedToSaveStaticPermissions(e);

                        throw; // Polly will catch it
                    }
                },
                (scope, logger),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    private async Task _PreCacheDynamicPermissionsAsync(AsyncServiceScope scope, CancellationToken cancellationToken)
    {
        if (!_options.IsDynamicPermissionStoreEnabled)
        {
            return;
        }

        var store = scope.ServiceProvider.GetRequiredService<IDynamicPermissionDefinitionStore>();

        try
        {
            // Pre-cache permissions, so first request doesn't wait
            await store.GetGroupsAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            logger.LogFailedToPreCacheDynamicPermissions(e);

            throw;
        }
    }
}

internal static partial class PermissionsInitializationBackgroundServiceLog
{
    [LoggerMessage(
        EventId = 1,
        EventName = "FailedToSaveStaticPermissions",
        Level = LogLevel.Error,
        Message = "Failed to save static permissions to the database"
    )]
    public static partial void LogFailedToSaveStaticPermissions(this ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 2,
        EventName = "FailedToPreCacheDynamicPermissions",
        Level = LogLevel.Error,
        Message = "Failed to pre-cache dynamic permissions"
    )]
    public static partial void LogFailedToPreCacheDynamicPermissions(this ILogger logger, Exception exception);
}
