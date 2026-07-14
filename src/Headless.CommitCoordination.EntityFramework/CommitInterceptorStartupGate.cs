// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.CommitCoordination.EntityFramework;

/// <summary>
/// Startup self-probe that verifies the commit-coordination EF interceptor actually fires for
/// <typeparamref name="TContext" /> — i.e. that the on-by-default transactional outbox is wired end-to-end. It runs
/// before any hosted service (<see cref="IHostedLifecycleService.StartingAsync" />) and, when the interceptor is not
/// firing, surfaces the silent "outbox enabled but mis-wired" footgun loudly per <see cref="CommitInterceptorProbeOptions" />.
/// </summary>
/// <remarks>
/// The probe is side-effect free: it opens a transaction, enlists commit coordination, registers a one-shot
/// <c>OnCommit</c> observer, and commits an <b>empty</b> transaction (no rows written → no consumer data mutated,
/// mirroring the SQL Server diagnostic self-probe). When the interceptor is attached, committing fires the
/// interceptor's commit edge → the scope is signalled committed → the observer runs. When the interceptor is NOT
/// attached, the commit succeeds but nothing signals the scope, so its dispose drains as a rollback and the
/// <c>OnCommit</c> observer never runs — that is the mis-wire the probe catches. Probing requires a reachable
/// database; a connection failure is treated as inconclusive (DB reachability is the storage initializer's concern,
/// not this gate's) and the host is allowed to start.
/// </remarks>
internal sealed partial class CommitInterceptorStartupGate<TContext>(
    IServiceProvider serviceProvider,
    IOptions<CommitInterceptorProbeOptions> options,
    ILogger<CommitInterceptorStartupGate<TContext>> logger
) : IHostedLifecycleService
    where TContext : DbContext
{
    private int _probeRan;

    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        // Run-once guard: a single boot must probe at most once even if the lifecycle hook is re-entered.
        if (Interlocked.Exchange(ref _probeRan, 1) == 1)
        {
            return;
        }

        var mode = options.Value.Mode;

        if (mode == CommitProbeMode.Disabled)
        {
            return;
        }

        bool committedObserved;

        try
        {
            await using var scope = serviceProvider.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<TContext>();

            // Run the probe through the context's execution strategy: a retrying strategy (EnableRetryOnFailure)
            // rejects a user-initiated BeginTransaction unless the whole unit runs through the strategy. Resolving
            // the context inside the try also keeps an unresolvable context (e.g. factory-only registration) on the
            // inconclusive path below instead of aborting host startup.
            committedObserved = await context
                .Database.CreateExecutionStrategy()
                .ExecuteAsync(
                    // ExecuteAsync awaits this lambda to completion before the outer `await using scope` disposes,
                    // so scope (and the context resolved from it) is always alive while the closure runs.
                    // ReSharper disable once AccessToDisposedClosure
                    async ct =>
                    {
                        await using var transaction = await context
                            .Database.BeginTransactionAsync(ct)
                            .ConfigureAwait(false);

                        var observed = false;

                        // Enlist synchronously in this frame so the ambient coordinator flows to the commit edge.
                        await using (
                            var commitScope = context.Database.EnlistCommitCoordination(
                                transaction,
                                scope.ServiceProvider,
                                ct
                            )
                        )
                        {
                            commitScope.Coordinator.OnCommit(
                                (_, _) =>
                                {
                                    observed = true;
                                    return ValueTask.CompletedTask;
                                }
                            );

                            // Empty transaction: commits no rows. If the interceptor is attached it observes this
                            // commit edge and signals the scope committed (draining the observer); if not, the
                            // dispose below drains as rollback and the observer never runs.
                            await transaction.CommitAsync(ct).ConfigureAwait(false);
                        }

                        return observed;
                    },
                    cancellationToken
                )
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Inconclusive — the context could not be resolved (e.g. factory-only registration), the database was
            // unreachable, a retrying execution strategy still could not run the probe, or the probe transaction
            // could not otherwise complete. None of these is the mis-wire signal (carried by
            // committedObserved == false, evaluated below and surfaced per mode), so the host is allowed to start.
            // Cancellation propagates so cooperative shutdown is honored.
            LogProbeInconclusive(logger, typeof(TContext).FullName, ex);
            return;
        }

        if (committedObserved)
        {
            return;
        }

        if (mode == CommitProbeMode.Strict)
        {
            throw new InvalidOperationException(
                $"Commit coordination is enabled for `{typeof(TContext).FullName}` but the commit interceptor did not "
                    + "fire — publishes are NOT atomic with the database transaction. Ensure the DbContext is "
                    + "registered through AddHeadlessDbContext, or that AddDiRegisteredInterceptors(sp) / "
                    + "AddInterceptors(sp.GetServices<IInterceptor>()) wired the interceptor in your AddDbContext "
                    + "options action."
            );
        }

        LogInterceptorNotFiring(logger, typeof(TContext).FullName);
    }

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Warning,
        Message = "Commit coordination is enabled for `{ContextType}` but the commit interceptor did not fire; "
            + "publishes are NOT atomic with the database transaction. Durable outbox rows are still relay-recovered, "
            + "but register the DbContext through AddHeadlessDbContext or call AddDiRegisteredInterceptors(sp) in your "
            + "AddDbContext options action to restore atomic dispatch."
    )]
    // ReSharper disable once InconsistentNaming
    private static partial void LogInterceptorNotFiring(ILogger logger, string? contextType);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Debug,
        Message = "Commit interceptor self-probe for `{ContextType}` was inconclusive (database unreachable); "
            + "skipping. The host is allowed to start."
    )]
    // ReSharper disable once InconsistentNaming
    private static partial void LogProbeInconclusive(ILogger logger, string? contextType, Exception exception);
}
