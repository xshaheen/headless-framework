// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Features.Definitions;
using Headless.Features.Models;
using Headless.Hosting.Initialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;

namespace Headless.Features.Seeders;

public sealed class FeaturesInitializationBackgroundService(
    TimeProvider timeProvider,
    IServiceScopeFactory serviceScopeFactory,
    IOptions<FeatureManagementOptions> optionsAccessor,
    ILogger<FeaturesInitializationBackgroundService> logger
) : IHostedService, IDisposable, IInitializer
{
    private readonly TaskCompletionSource _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly FeatureManagementOptions _options = optionsAccessor.Value;
    private CancellationTokenSource? _linkedCts;
    private Task? _initializeDynamicFeaturesTask;

    public bool IsInitialized => _tcs.Task.IsCompletedSuccessfully;

    public Task WaitForInitializationAsync(CancellationToken cancellationToken = default) =>
        _tcs.Task.WaitAsync(cancellationToken);

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_options is { SaveStaticFeaturesToDatabase: false, IsDynamicFeatureStoreEnabled: false })
        {
            // Both features disabled — initialization is a no-op; signal completion immediately.
            _tcs.TrySetResult();

            return Task.CompletedTask;
        }

        _linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cancellationTokenSource.Token);
        _initializeDynamicFeaturesTask = _InitializeDynamicFeaturesAsync(_linkedCts.Token);

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _cancellationTokenSource.CancelAsync().ConfigureAwait(false);

        if (_initializeDynamicFeaturesTask is not null)
        {
            try
            {
#pragma warning disable VSTHRD003 // IHostedService pattern: task started in StartAsync, awaited in StopAsync
                await _initializeDynamicFeaturesTask.WaitAsync(cancellationToken).ConfigureAwait(false);
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

    private async Task _InitializeDynamicFeaturesAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            await using var scope = serviceScopeFactory.CreateAsyncScope();

            await _SaveStaticFeaturesToDatabaseAsync(scope, cancellationToken).ConfigureAwait(false);

            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            await _PreCacheDynamicFeaturesAsync(scope, cancellationToken).ConfigureAwait(false);

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

    private async Task _SaveStaticFeaturesToDatabaseAsync(AsyncServiceScope scope, CancellationToken cancellationToken)
    {
        if (!_options.SaveStaticFeaturesToDatabase)
        {
            return;
        }

        var options = new RetryStrategyOptions
        {
            Name = "SaveStaticFeatureToDatabaseRetry",
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

                var store = scope.ServiceProvider.GetRequiredService<IDynamicFeatureDefinitionStore>();

                try
                {
                    await store.SaveAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    logger.LogError(e, "Failed to save static features to the database");

                    throw; // Polly will catch it
                }
            },
            (scope, logger),
            cancellationToken
        );
    }

    private async Task _PreCacheDynamicFeaturesAsync(AsyncServiceScope scope, CancellationToken cancellationToken)
    {
        if (!_options.IsDynamicFeatureStoreEnabled)
        {
            return;
        }

        var store = scope.ServiceProvider.GetRequiredService<IDynamicFeatureDefinitionStore>();

        try
        {
            // Pre-cache features, so the first request doesn't wait
            await store.GetGroupsAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to pre-cache dynamic features");

            throw;
        }
    }
}
