// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace System.Diagnostics;

/// <summary>Extension methods for running a <see cref="Process"/> from a <see cref="ProcessStartInfo"/> as a task or an asynchronous output stream, plus safe termination.</summary>
public static class HeadlessProcessExtensions
{
    /// <summary>
    /// Executes a process asynchronously based on the provided <see cref="ProcessStartInfo"/> configuration
    /// and waits for its completion while supporting cancellation.
    /// </summary>
    /// <param name="psi">The <see cref="ProcessStartInfo"/> containing the configuration for starting the process, such as file path, arguments, and redirections.</param>
    /// <param name="cancellationToken">A token to observe for cancellation of the process execution.</param>
    /// <returns>
    /// A <see cref="Task{TResult}"/> that represents the completion of the process execution.
    /// The result contains a <see cref="ProcessResult"/> with the exit code and captured standard output/error logs.
    /// </returns>
    /// <exception cref="InvalidOperationException">Thrown when the process cannot start.</exception>
    /// <exception cref="OperationCanceledException">Thrown if the operation is canceled via the provided <paramref name="cancellationToken"/>.</exception>
    public static async Task<ProcessResult> RunAsTaskAsync(
        this ProcessStartInfo psi,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        int exitCode;
        var logs = new List<ProcessOutput>();

        using (var process = new Process())
        {
            process.StartInfo = psi;

            if (psi.RedirectStandardError)
            {
                process.ErrorDataReceived += (_, e) =>
                {
                    if (e.Data is null)
                    {
                        return;
                    }

                    lock (logs)
                    {
                        logs.Add(new ProcessOutput(ProcessOutputType.StandardError, e.Data));
                    }
                };
            }

            if (psi.RedirectStandardOutput)
            {
                process.OutputDataReceived += (_, e) =>
                {
                    if (e.Data is null)
                    {
                        return;
                    }

                    lock (logs)
                    {
                        logs.Add(new ProcessOutput(ProcessOutputType.StandardOutput, e.Data));
                    }
                };
            }

            if (!process.Start())
            {
                throw new InvalidOperationException("Cannot start the process");
            }

            if (psi.RedirectStandardError)
            {
                process.BeginErrorReadLine();
            }

            if (psi.RedirectStandardOutput)
            {
                process.BeginOutputReadLine();
            }

            if (psi.RedirectStandardInput)
            {
                process.StandardInput.Close();
            }

            CancellationTokenRegistration registration = default;

            try
            {
                if (cancellationToken.CanBeCanceled && !process.HasExited)
                {
                    // ReSharper disable once AccessToDisposedClosure
                    registration = cancellationToken.Register(process.TryToKill);
                }

                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                await registration.DisposeAsync().ConfigureAwait(false);
            }

            // Drain any buffered stdout/stderr callbacks before reading the exit code and logs.
            // WaitForExitAsync returns as soon as the process exits, but async stdio event callbacks
            // may still be in-flight; the no-argument WaitForExit() blocks until all redirected streams
            // have been fully read, so trailing output is captured in the logs list.
#pragma warning disable CA1849 // The process has exited, so this only drains redirected stdio, which WaitForExitAsync does not guarantee.
            process.WaitForExit();
#pragma warning restore CA1849

            exitCode = process.ExitCode;
        }

        cancellationToken.ThrowIfCancellationRequested();

        return new ProcessResult(exitCode, logs);
    }

    /// <summary>
    /// Runs the process described by <paramref name="psi"/> and streams its standard output/error lines as they are
    /// printed, followed by a final exit-code item.
    /// </summary>
    /// <param name="psi">The <see cref="ProcessStartInfo"/> describing the process to start and which streams to redirect.</param>
    /// <param name="cancellationToken">A token that, when canceled, terminates the process tree and ends the enumeration.</param>
    /// <returns>
    /// An <see cref="IAsyncEnumerable{T}"/> that starts the process when enumeration begins and yields each standard
    /// output/error line in arrival order, then exactly one <see cref="ProcessStreamedOutputType.ExitCode"/> item.
    /// Ending the enumeration early, by breaking out of the loop or by cancellation, terminates the process tree.
    /// </returns>
    /// <exception cref="InvalidOperationException">Thrown during enumeration when the process cannot start.</exception>
    /// <exception cref="OperationCanceledException">Thrown during enumeration when <paramref name="cancellationToken"/> is canceled.</exception>
    public static async IAsyncEnumerable<ProcessStreamedOutput> RunAndStreamAsync(
        this ProcessStartInfo psi,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        // stdout and stderr DataReceived callbacks fire on independent thread-pool threads; an unbounded channel
        // accepts concurrent writers and never rejects TryWrite, so neither callback can drop or block on a line.
        var channel = Channel.CreateUnbounded<ProcessStreamedOutput>(
            new UnboundedChannelOptions { SingleReader = true }
        );

        var process = new Process();
        Task? exited = null;

        try
        {
            process.StartInfo = psi;

            if (psi.RedirectStandardError)
            {
                process.ErrorDataReceived += (_, e) =>
                {
                    if (e.Data is not null)
                    {
                        channel.Writer.TryWrite(
                            new ProcessStreamedOutput(ProcessStreamedOutputType.StandardError, e.Data)
                        );
                    }
                };
            }

            if (psi.RedirectStandardOutput)
            {
                process.OutputDataReceived += (_, e) =>
                {
                    if (e.Data is not null)
                    {
                        channel.Writer.TryWrite(
                            new ProcessStreamedOutput(ProcessStreamedOutputType.StandardOutput, e.Data)
                        );
                    }
                };
            }

            if (!process.Start())
            {
                throw new InvalidOperationException("Cannot start the process");
            }

            if (psi.RedirectStandardError)
            {
                process.BeginErrorReadLine();
            }

            if (psi.RedirectStandardOutput)
            {
                process.BeginOutputReadLine();
            }

            if (psi.RedirectStandardInput)
            {
                process.StandardInput.Close();
            }

#pragma warning disable CA2025 // False positive: the finally block awaits this task before it disposes the process, on every path.
            exited = _CompleteOnExitAsync(process, channel.Writer);
#pragma warning restore CA2025

            await foreach (var item in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return item;
            }
        }
        finally
        {
            if (exited is not null)
            {
                // The consumer stopped before the process exited: by cancellation, an exception, or breaking out of
                // the loop. Kill the tree so it does not outlive the enumeration, then wait so the process is not
                // disposed while its stdio callbacks are still draining.
                if (!exited.IsCompleted)
                {
                    process.TryToKill();
                }

                await exited.ConfigureAwait(false);
            }

            process.Dispose();
        }
    }

    private static async Task _CompleteOnExitAsync(Process process, ChannelWriter<ProcessStreamedOutput> writer)
    {
        try
        {
            await process.WaitForExitAsync().ConfigureAwait(false);

            // WaitForExitAsync returns as soon as the process exits, but async stdio event callbacks may still be
            // in-flight. The no-argument WaitForExit() blocks until all redirected streams have been fully read, so
            // the exit-code item is always the last one written.
#pragma warning disable CA1849 // Synchronous WaitForExit() is intentional: the async overload does not guarantee redirected stdio has drained.
            process.WaitForExit();
#pragma warning restore CA1849

            writer.TryWrite(
                new ProcessStreamedOutput(
                    ProcessStreamedOutputType.ExitCode,
                    process.ExitCode.ToString(CultureInfo.InvariantCulture)
                )
            );

            writer.TryComplete();
        }
        catch (Exception exception)
        {
            // Hand any failure to the reader instead of losing it on an unobserved task.
            writer.TryComplete(exception);
        }
    }

    /// <summary>
    /// Attempts to terminate the specified process and its entire process tree.
    /// If unable to terminate the entire tree, it will attempt to kill only the root process.
    /// </summary>
    /// <param name="process">The instance of the <see cref="Process"/> to be terminated.</param>
    public static void TryToKill(this Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception exception) when (_IsExpectedKillFailure(exception))
        {
            try
            {
                // Try to at least kill the root process
                process.Kill();
            }
            catch (Exception retryException) when (_IsExpectedKillFailure(retryException))
            {
                // Ignore
            }
        }
    }

    private static bool _IsExpectedKillFailure(Exception exception)
    {
        return exception is InvalidOperationException or Win32Exception or NotSupportedException or AggregateException;
    }
}
