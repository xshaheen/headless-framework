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
    private Task? _initializeDynamicPermissionsTask;

    public bool IsInitialized => _tcs.Task.IsCompletedSuccessfully;

    public Task WaitForInitializationAsync(CancellationToken cancellationToken = default) =>
        _tcs.Task.WaitAsync(cancellationToken);

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_options is { SaveStaticPermissionsToDatabase: false, IsDynamicPermissionStoreEnabled: false })
        {
            // Both features disabled — initialization is a no-op; signal completion immediately.
            _tcs.TrySetResult();

            return Task.CompletedTask;
        }

        _initializeDynamicPermissionsTask = _InitializeDynamicPermissionsAsync(cancellationToken);

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
                await _initializeDynamicPermissionsTask.ConfigureAwait(false);
#pragma warning restore VSTHRD003
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Background initialization task faulted");
            }
        }
    }

    public void Dispose()
    {
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

            await _SaveStaticPermissionsToDatabaseAsync(scope, cancellationToken);

            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            await _PreCacheDynamicPermissionsAsync(scope, cancellationToken);

            _tcs.TrySetResult();
        }
        catch (OperationCanceledException)
        {
            // Shutdown before completion — leave TCS incomplete so waiters get a timeout.
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

        await pipeline.ExecuteAsync(
            static async (state, cancellationToken) =>
            {
                var (scope, logger) = state;

                var store = scope.ServiceProvider.GetRequiredService<IDynamicPermissionDefinitionStore>();

                try
                {
                    await store.SaveAsync(cancellationToken);
                }
                catch (Exception e)
                {
                    logger.LogError(e, "Failed to save static permissions to the database");

                    throw; // Polly will catch it
                }
            },
            (scope, logger),
            cancellationToken
        );
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
            await store.GetGroupsAsync(cancellationToken);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to pre-cache dynamic permissions");

            throw;
        }
    }
}
