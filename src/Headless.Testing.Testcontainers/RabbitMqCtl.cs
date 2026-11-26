// Copyright (c) Mahmoud Shaheen. All rights reserved.

using DotNet.Testcontainers.Containers;

namespace Headless.Testing.Testcontainers;

/// <summary>Runs <c>rabbitmqctl</c> in a RabbitMQ container, bounded so a stalled call fails instead of hanging.</summary>
/// <remarks>
/// A <c>rabbitmqctl</c> process occasionally stalls at startup and never reaches the node, and a container exec waits
/// for the process to exit with no bound of its own. Without a bound, one stalled call holds the test run until the
/// runner's hang timeout aborts the whole module. A stalled read-only <c>list_*</c> call is asked again, because asking
/// again cannot change broker state; a command that changes state gets one attempt, so a stall fails it.
/// </remarks>
internal static class RabbitMqCtl
{
    /// <summary>How long one call may run. A healthy call answers in well under a second, even on a loaded host.</summary>
    internal static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(30);

    /// <summary>How many times a read-only call runs before a stall fails it. Each attempt gets an equal share of the bound.</summary>
    internal const int ReadOnlyAttempts = 3;

    /// <summary>Runs <c>rabbitmqctl</c> with <paramref name="arguments"/> in <paramref name="container"/>.</summary>
    /// <exception cref="TimeoutException">The call did not finish within <paramref name="timeout"/>.</exception>
    public static async Task<ExecResult> ExecAsync(
        IContainer container,
        IList<string> arguments,
        CancellationToken cancellationToken,
        TimeSpan? timeout = null
    )
    {
        var bound = timeout ?? CallTimeout;
        var attempts = IsReadOnly(arguments) ? ReadOnlyAttempts : 1;
        var perAttempt = bound / attempts;

        for (var attempt = 1; ; attempt++)
        {
            using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            bounded.CancelAfter(perAttempt);

            try
            {
                return await container.ExecAsync(["rabbitmqctl", .. arguments], bounded.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException e) when (!cancellationToken.IsCancellationRequested)
            {
                if (attempt < attempts)
                {
                    continue;
                }

                throw new TimeoutException(
                    $"rabbitmqctl {string.Join(' ', arguments)} did not finish within {bound} ({attempts} attempt(s)).",
                    e
                );
            }
        }
    }

    /// <summary>Whether the command only reads broker state, so running it again after a stall is safe.</summary>
    internal static bool IsReadOnly(IList<string> arguments) =>
        arguments.Count > 0 && arguments[0].StartsWith("list_", StringComparison.Ordinal);
}
