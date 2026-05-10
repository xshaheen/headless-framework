// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.ExceptionServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Headless.Messaging.Internal;

/// <summary>
/// Wraps a publish operation with the registered <see cref="IPublishFilter"/> chain.
/// Both <see cref="DirectPublisher"/> and <see cref="OutboxPublisher"/> route through this pipeline so
/// filter coverage is uniform across direct and outbox publish paths.
/// </summary>
internal interface IPublishExecutionPipeline
{
    /// <summary>
    /// Runs the registered publish filters around the supplied <paramref name="innerPublish"/> delegate.
    /// </summary>
    /// <param name="content">The message payload (may be <see langword="null"/>).</param>
    /// <param name="options">The caller-supplied <see cref="PublishOptions"/>.</param>
    /// <param name="delayTime">The scheduled delay; <see langword="null"/> for immediate publishes.</param>
    /// <param name="innerPublish">
    /// Publisher-specific tail invoked after the executing-phase filters complete. Receives the
    /// filter-mutated options and delay so a filter that reassigns either is honored downstream.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ExecuteAsync<T>(
        T? content,
        PublishOptions? options,
        TimeSpan? delayTime,
        Func<PublishOptions?, TimeSpan?, CancellationToken, Task> innerPublish,
        CancellationToken cancellationToken = default
    );
}

internal sealed class PublishExecutionPipeline(
    IServiceProvider serviceProvider,
    ILogger<PublishExecutionPipeline>? logger = null
) : IPublishExecutionPipeline
{
    public async Task ExecuteAsync<T>(
        T? content,
        PublishOptions? options,
        TimeSpan? delayTime,
        Func<PublishOptions?, TimeSpan?, CancellationToken, Task> innerPublish,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Pipeline is Singleton (matching IConsumeExecutionPipeline at Setup.cs:111) — both publishers
        // it serves are also Singleton, so a Scoped pipeline would be a captive dependency. Per-publish
        // scope is created here so scoped IPublishFilter instances resolve independently per call.
        await using var scope = serviceProvider.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        var filters = provider.GetServices<IPublishFilter>().ToArray();
        var enteredCount = 0;

        var ctx = new PublishingContext(content, typeof(T), options, delayTime);

        try
        {
            for (var i = 0; i < filters.Length; i++)
            {
                // Increment before await so a filter throwing during executing still gets its exception phase.
                enteredCount = i + 1;
                await filters[i].OnPublishExecutingAsync(ctx).ConfigureAwait(false);
            }

            await innerPublish(ctx.Options, ctx.DelayTime, cancellationToken).ConfigureAwait(false);

            var executedCtx = new PublishedContext(content, typeof(T), ctx.Options, ctx.DelayTime);
            for (var i = filters.Length - 1; i >= 0; i--)
            {
                try
                {
                    await filters[i].OnPublishExecutedAsync(executedCtx).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    // The message was already accepted by the transport/outbox. Propagating an
                    // after-success filter failure would invite callers to retry and duplicate it.
                    logger?.PublishExecutedFilterFailed(e, filters[i].GetType().FullName ?? filters[i].GetType().Name);
                }
            }
        }
        catch (Exception e)
        {
            if (enteredCount == 0)
            {
                throw;
            }

            var exCtx = new PublishExceptionContext(content, typeof(T), ctx.Options, ctx.DelayTime, e);
            for (var i = enteredCount - 1; i >= 0; i--)
            {
                await filters[i].OnPublishExceptionAsync(exCtx).ConfigureAwait(false);
            }

            if (!exCtx.ExceptionHandled)
            {
                ExceptionDispatchInfo.Capture(exCtx.Exception).Throw();
            }
        }
    }
}
