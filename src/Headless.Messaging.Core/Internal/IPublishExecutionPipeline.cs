// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.ExceptionServices;
using Headless.Checks;
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
    /// <param name="isTransactional">
    /// When <see langword="true"/>, the publish was committed inside an ambient outbox transaction
    /// whose commit is the caller's responsibility — the post-success <see cref="PublishedContext"/>
    /// surfaces this through <see cref="PublishedContext.IsTransactional"/>. Defaults to
    /// <see langword="false"/> (direct publish or AutoCommit outbox).
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ExecuteAsync<T>(
        T? content,
        PublishOptions? options,
        TimeSpan? delayTime,
        Func<PublishOptions?, TimeSpan?, CancellationToken, Task> innerPublish,
        bool isTransactional = false,
        CancellationToken cancellationToken = default
    );
}

internal sealed class PublishExecutionPipeline(
    IServiceProvider serviceProvider,
    ILogger<PublishExecutionPipeline>? logger = null
) : IPublishExecutionPipeline
{
    private readonly IServiceProvider _serviceProvider = Argument.IsNotNull(serviceProvider);

    public async Task ExecuteAsync<T>(
        T? content,
        PublishOptions? options,
        TimeSpan? delayTime,
        Func<PublishOptions?, TimeSpan?, CancellationToken, Task> innerPublish,
        bool isTransactional = false,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Pipeline is Singleton (matching IConsumeExecutionPipeline at Setup.cs:111) — both publishers
        // it serves are also Singleton, so a Scoped pipeline would be a captive dependency. Per-publish
        // scope is created here so scoped IPublishFilter instances resolve independently per call.
        await using var scope = _serviceProvider.CreateAsyncScope();
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
                await filters[i].OnPublishExecutingAsync(ctx, cancellationToken).ConfigureAwait(false);
            }

            await innerPublish(ctx.Options, ctx.DelayTime, cancellationToken).ConfigureAwait(false);

            var executedCtx = new PublishedContext(content, typeof(T), ctx.Options, ctx.DelayTime)
            {
                IsTransactional = isTransactional,
            };
            for (var i = filters.Length - 1; i >= 0; i--)
            {
                try
                {
                    await filters[i].OnPublishExecutedAsync(executedCtx, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    // The message was already accepted by the transport/outbox. Propagating an
                    // after-success filter failure — including OperationCanceledException — would
                    // invite callers to retry and duplicate it. Cancellation has no operational
                    // meaning once the inner work has committed.
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
                try
                {
                    await filters[i].OnPublishExceptionAsync(exCtx, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception nested)
                {
                    // Preserve the original exception identity for the eventual rethrow. A throwing
                    // exception-phase filter must not silently replace the original failure or skip
                    // the outer filters in the chain.
                    logger?.PublishExceptionFilterFailed(
                        nested,
                        filters[i].GetType().FullName ?? filters[i].GetType().Name
                    );
                }
            }

            // Cancellation is never swallowable: ignore ExceptionHandled when the original failure
            // was an OperationCanceledException so callers always observe the cancel.
            if (!exCtx.ExceptionHandled || exCtx.Exception is OperationCanceledException)
            {
                ExceptionDispatchInfo.Capture(exCtx.Exception).Throw();
            }
        }
    }
}
