// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections;
using System.ComponentModel;
using System.Reactive.Linq;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace System.Diagnostics;

/// <summary>Extension methods for running a <see cref="Process"/> from a <see cref="ProcessStartInfo"/> as a task or observable, plus safe termination.</summary>
public static class ProcessExtensions
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
            // Process has already exited (WaitForExitAsync awaited above), so the synchronous WaitForExit() returns
            // immediately after draining buffered stdio — no deadlock risk; the async overload does not guarantee
            // redirected streams have fully drained.
#pragma warning disable CA1849, AsyncFixer02, MA0042
            process.WaitForExit();
#pragma warning restore CA1849, AsyncFixer02, MA0042

            exitCode = process.ExitCode;
        }

        cancellationToken.ThrowIfCancellationRequested();

        return new ProcessResult(exitCode, logs);
    }

    /// <summary>
    /// Runs the process described by <paramref name="psi"/> and exposes its standard output/error and exit code as an
    /// observable sequence, allowing reactive-style consumption of the streamed lines.
    /// </summary>
    /// <param name="psi">The <see cref="ProcessStartInfo"/> describing the process to start and which streams to redirect.</param>
    /// <returns>
    /// An <see cref="IObservable{T}"/> that streams the process output.
    /// OnNext: each standard output/error line as it is printed, then a final exit-code item.
    /// OnError: an <see cref="InvalidOperationException"/> when the process cannot be started.
    /// </returns>
    public static IObservable<ProcessObservedOutput> RunAsObservable(this ProcessStartInfo psi)
    {
        return Observable.Create<ProcessObservedOutput>(
            async (observer, cancellationToken) =>
            {
                using var process = new Process();

                process.StartInfo = psi;

                // stdout and stderr DataReceived callbacks fire on independent thread-pool threads. Rx requires
                // OnNext to be serialized, so gate both handlers on a shared lock to honor the observer grammar.
                var gate = new Lock();

                if (psi.RedirectStandardError)
                {
                    process.ErrorDataReceived += (_, e) =>
                    {
                        if (e.Data is null)
                        {
                            return;
                        }

                        lock (gate)
                        {
                            observer.OnNext(new ProcessObservedOutput(ProcessObservedOutputType.StandardError, e.Data));
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

                        lock (gate)
                        {
                            observer.OnNext(
                                new ProcessObservedOutput(ProcessObservedOutputType.StandardOutput, e.Data)
                            );
                        }
                    };
                }

                if (!process.Start())
                {
                    observer.OnError(new InvalidOperationException("Cannot start the process"));

                    return;
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
                        registration = cancellationToken.Register(process.TryToKill);
                    }

                    await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    await registration.DisposeAsync().ConfigureAwait(false);
                }

                // Drain any buffered stdout/stderr callbacks before signalling completion.
                // WaitForExitAsync returns as soon as the process exits, but async stdio event
                // callbacks may still be in-flight. The no-argument WaitForExit() blocks until
                // all redirected streams have been fully read, ensuring OnNext is never called
                // after OnCompleted (which would violate the Rx contract).
#pragma warning disable CA1849, MA0042 // Synchronous WaitForExit() is intentional: the async overload does not guarantee redirected stdio has drained.
                process.WaitForExit();
#pragma warning restore CA1849, MA0042

                observer.OnNext(
                    new ProcessObservedOutput(
                        ProcessObservedOutputType.ExitCode,
                        process.ExitCode.ToString(CultureInfo.InvariantCulture)
                    )
                );

                observer.OnCompleted();
            }
        );
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

#region Run As Observable Return Types

/// <summary>A single item streamed by <see cref="ProcessExtensions.RunAsObservable(ProcessStartInfo)"/>: an output line or the final exit code.</summary>
/// <param name="Type">Whether this item is standard output, standard error, or the exit code.</param>
/// <param name="Text">The output line text, or the exit code rendered as a string.</param>
public sealed record ProcessObservedOutput(ProcessObservedOutputType Type, string Text)
{
    /// <summary>Returns the text, prefixed with <c>"error: "</c> for standard-error items.</summary>
    /// <returns>The formatted representation of this item.</returns>
    public override string ToString()
    {
        return Type switch
        {
            ProcessObservedOutputType.StandardError => "error: " + Text,
            _ => Text,
        };
    }
}

/// <summary>Identifies the kind of item produced by <see cref="ProcessExtensions.RunAsObservable(ProcessStartInfo)"/>.</summary>
public enum ProcessObservedOutputType
{
    /// <summary>A line written to standard output.</summary>
    StandardOutput,

    /// <summary>A line written to standard error.</summary>
    StandardError,

    /// <summary>The process exit code, reported once the process terminates.</summary>
    ExitCode,
}

#endregion

#region Run As Task Return Types

/// <summary>The result of running a process to completion via <see cref="ProcessExtensions.RunAsTaskAsync(ProcessStartInfo,CancellationToken)"/>.</summary>
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
    public IEnumerator<ProcessOutput> GetEnumerator() => _output.GetEnumerator();

    [MustDisposeResource]
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

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
public enum ProcessOutputType
{
    /// <summary>A line written to standard output.</summary>
    StandardOutput,

    /// <summary>A line written to standard error.</summary>
    StandardError,
}

#endregion
