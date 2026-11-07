// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Dashboard;

/// <summary>
/// Calls one capability on every loaded module the user may view, in parallel, each with its own timeout. A module that
/// times out or throws answers <c>Stale</c> in its place, so one slow or broken module never holds up or breaks the
/// others.
/// </summary>
internal sealed class ModuleFanOut(
    DashboardManifestProvider manifest,
    IOptions<HeadlessDashboardOptions> options,
    TimeProvider timeProvider,
    ILogger<ModuleFanOut> logger
)
{
    /// <summary>
    /// Calls <paramref name="call"/> on every loaded module that implements <typeparamref name="TSource"/> and that
    /// <paramref name="access"/> lets the user view, and returns one answer per module in registration order. A refused module is
    /// never called.
    /// </summary>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public async Task<IReadOnlyList<ModuleAnswer<TResult>>> CallAsync<TSource, TResult>(
        DashboardSessionSnapshot access,
        string capability,
        Func<TSource, CancellationToken, Task<TResult>> call,
        CancellationToken cancellationToken
    )
        where TSource : class
    {
        Argument.IsNotNull(access);
        Argument.IsNotNull(call);

        var calls = new List<Task<ModuleAnswer<TResult>>>();

        foreach (var module in manifest.Manifest.Modules)
        {
            if (module.IsRefused || module.Module is not TSource source)
            {
                continue;
            }

            if (!access.MayView(module.Id))
            {
                continue;
            }

            calls.Add(_CallOneAsync(module, source, capability, call, cancellationToken));
        }

        return await Task.WhenAll(calls).ConfigureAwait(false);
    }

    private async Task<ModuleAnswer<TResult>> _CallOneAsync<TSource, TResult>(
        MergedModule module,
        TSource source,
        string capability,
        Func<TSource, CancellationToken, Task<TResult>> call,
        CancellationToken cancellationToken
    )
    {
        var timeout = options.Value.AggregationTimeout;

        // The timer starts before the module is called, so the wait is bounded from the call itself.
        using var timeoutSource = new CancellationTokenSource(timeout, timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);
        Task<TResult>? task = null;

        try
        {
            task = call(source, linked.Token);

            // WaitAsync stops the wait even when the module ignores its token, so a stuck module cannot hold the
            // response past the timeout.
            var result = await task.WaitAsync(linked.Token).ConfigureAwait(false);

            return new ModuleAnswer<TResult>(module, result, Stale: false);
        }
        catch (OperationCanceledException)
            when (timeoutSource.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            logger.ModuleTimedOut(module.Id, capability, timeout);
            ObserveLateFailure(task);

            return new ModuleAnswer<TResult>(module, default, Stale: true);
        }
        catch (Exception e) when (!cancellationToken.IsCancellationRequested)
        {
            // A failing module is this boundary's whole policy: log it and show the module as stale, so the other
            // modules still answer.
            logger.ModuleFailed(e, module.Id, capability);

            return new ModuleAnswer<TResult>(module, default, Stale: true);
        }
    }

    // A module abandoned at the timeout may still fail later; observing it keeps that failure out of
    // TaskScheduler.UnobservedTaskException.
    internal static void ObserveLateFailure(Task? task)
    {
        if (task is null)
        {
            return;
        }

        _ = task.ContinueWith(
            static completed => _ = completed.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default
        );
    }
}

/// <summary>One module's answer in a fan-out: its result, or <c>Stale</c> when it timed out or failed.</summary>
internal sealed record ModuleAnswer<TResult>(MergedModule Module, TResult? Result, bool Stale);

internal static partial class ModuleFanOutLog
{
    [LoggerMessage(
        EventId = 1,
        EventName = "DashboardModuleTimedOut",
        Level = LogLevel.Warning,
        Message = "Dashboard module {ModuleId} did not answer {Capability} within {Timeout}; it is shown as stale"
    )]
    internal static partial void ModuleTimedOut(
        this ILogger logger,
        string moduleId,
        string capability,
        TimeSpan timeout
    );

    [LoggerMessage(
        EventId = 2,
        EventName = "DashboardModuleFailed",
        Level = LogLevel.Warning,
        Message = "Dashboard module {ModuleId} failed to answer {Capability}; it is shown as stale"
    )]
    internal static partial void ModuleFailed(
        this ILogger logger,
        Exception exception,
        string moduleId,
        string capability
    );
}
