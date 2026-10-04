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

#region Run And Stream Return Types

/// <summary>A single item streamed by <see cref="HeadlessProcessExtensions.RunAndStreamAsync(ProcessStartInfo,CancellationToken)"/>: an output line or the final exit code.</summary>
/// <param name="Type">Whether this item is standard output, standard error, or the exit code.</param>
/// <param name="Text">The output line text, or the exit code rendered as a string.</param>
public sealed record ProcessStreamedOutput(ProcessStreamedOutputType Type, string Text)
{
    /// <summary>Returns the text, prefixed with <c>"error: "</c> for standard-error items.</summary>
    /// <returns>The formatted representation of this item.</returns>
    public override string ToString()
    {
        return Type switch
        {
            ProcessStreamedOutputType.StandardError => "error: " + Text,
            _ => Text,
        };
    }
}

/// <summary>Identifies the kind of item produced by <see cref="HeadlessProcessExtensions.RunAndStreamAsync(ProcessStartInfo,CancellationToken)"/>.</summary>
/// <remarks>Additional members may be added in future versions; consumers switching on this enum should include a default case.</remarks>
[PublicAPI]
public enum ProcessStreamedOutputType
{
    /// <summary>A line written to standard output.</summary>
    StandardOutput = 0,

    /// <summary>A line written to standard error.</summary>
    StandardError = 1,

    /// <summary>The process exit code, reported once the process terminates.</summary>
    ExitCode = 2,
}

#endregion

#region Run As Task Return Types

/// <summary>The result of running a process to completion via <see cref="HeadlessProcessExtensions.RunAsTaskAsync(ProcessStartInfo,CancellationToken)"/>.</summary>
/// <param name="exitCode">The process exit code.</param>
/// <param name="output">The captured standard output and error lines, in the order they were received.</param>
public sealed class ProcessResult(int exitCode, IReadOnlyList<ProcessOutput> output)
{
    /// <summary>Gets the process exit code.</summary>
    public int ExitCode { get; } = exitCode;

    /// <summary>Gets the captured standard output and error lines.</summary>
    public ProcessOutputCollection Output { get; } = new(output);
}

/// <summary>A single captured output line from a completed process run.</summary>
/// <param name="Type">Whether this line came from standard output or standard error.</param>
/// <param name="Text">The output line text.</param>
public sealed record ProcessOutput(ProcessOutputType Type, string Text)
{
    /// <summary>Returns the text, prefixed with <c>"error: "</c> for standard-error lines.</summary>
    /// <returns>The formatted representation of this line.</returns>
    public override string ToString()
    {
        return Type switch
        {
            ProcessOutputType.StandardError => "error: " + Text,
            _ => Text,
        };
    }
}

/// <summary>A read-only, ordered collection of <see cref="ProcessOutput"/> lines captured from a process run.</summary>
public sealed class ProcessOutputCollection : IReadOnlyList<ProcessOutput>
{
    private readonly IReadOnlyList<ProcessOutput> _output;

    internal ProcessOutputCollection(IReadOnlyList<ProcessOutput> output)
    {
        _output = output;
    }

    /// <summary>Gets the number of captured output lines.</summary>
    public int Count => _output.Count;

    /// <summary>Gets the captured output line at the specified <paramref name="index"/>.</summary>
    /// <param name="index">The zero-based index of the line to retrieve.</param>
    /// <returns>The <see cref="ProcessOutput"/> at <paramref name="index"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="index"/> is outside the bounds of the collection.</exception>
    public ProcessOutput this[int index] => _output[index];

    /// <summary>Returns an enumerator over the captured output lines.</summary>
    /// <returns>An enumerator for the collection.</returns>
    [MustDisposeResource]
    public IEnumerator<ProcessOutput> GetEnumerator()
    {
        return _output.GetEnumerator();
    }

    [MustDisposeResource]
    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    /// <summary>Concatenates every captured line, separated by blank lines, into a single string.</summary>
    /// <returns>All captured output lines joined together.</returns>
    public override string ToString()
    {
        var sb = new StringBuilder();

        foreach (var item in _output)
        {
            sb.Append(item).AppendLine().AppendLine();
        }

        return sb.ToString();
    }
}

/// <summary>Identifies which standard stream a captured <see cref="ProcessOutput"/> line came from.</summary>
/// <remarks>Additional members may be added in future versions; consumers switching on this enum should include a default case.</remarks>
[PublicAPI]
public enum ProcessOutputType
{
    /// <summary>A line written to standard output.</summary>
    StandardOutput = 0,

    /// <summary>A line written to standard error.</summary>
    StandardError = 1,
}

#endregion
