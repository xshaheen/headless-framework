// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections;
using System.Reactive.Linq;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace System.Diagnostics;

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

                await process.WaitForExitAsync(cancellationToken).AnyContext();
            }
            finally
            {
                await registration.DisposeAsync().AnyContext();
            }

            exitCode = process.ExitCode;
        }

        cancellationToken.ThrowIfCancellationRequested();

        return new ProcessResult(exitCode, logs);
    }

    /// <summary>
    /// Converts an asynchronous operation or task into an observable sequence,
    /// allowing for reactive-style usage and composition of the resulting data or notifications.
    /// </summary>
    /// <returns>
    /// An IObservable sequence that represents the completion or result of the asynchronous operation.
    /// OnNext: It returns the process standard output/error as it printed.
    /// OnError: It returns <see cref="InvalidOperationException"/> when cannot start the process.
    /// </returns>
    public static IObservable<ProcessObservedOutput> RunAsObservable(this ProcessStartInfo psi)
    {
        return Observable.Create<ProcessObservedOutput>(
            async (observer, cancellationToken) =>
            {
                using var process = new Process();

                process.StartInfo = psi;

                if (psi.RedirectStandardError)
                {
                    process.ErrorDataReceived += (_, e) =>
                    {
                        if (e.Data is null)
                        {
                            return;
                        }

                        observer.OnNext(new ProcessObservedOutput(ProcessObservedOutputType.StandardError, e.Data));
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

                        observer.OnNext(new ProcessObservedOutput(ProcessObservedOutputType.StandardOutput, e.Data));
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
                        // ReSharper disable once AccessToDisposedClosure
                        registration = cancellationToken.Register(() => process.TryToKill());
                    }

                    await process.WaitForExitAsync(cancellationToken).AnyContext();
                }
                finally
                {
                    await registration.DisposeAsync().AnyContext();
                }

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
        catch (InvalidOperationException)
        {
            try
            {
                // Try to at least kill the root process
                process.Kill();
            }
            catch (InvalidOperationException)
            {
                // Ignore
            }
        }
    }
}

#region Run As Observable Return Types

public sealed record ProcessObservedOutput(ProcessObservedOutputType Type, string Text)
{
    public override string ToString()
    {
        return Type switch
        {
            ProcessObservedOutputType.StandardError => "error: " + Text,
            _ => Text,
        };
    }
}

public enum ProcessObservedOutputType
{
    StandardOutput,
    StandardError,
    ExitCode,
}

#endregion

#region Run As Task Return Types

public sealed class ProcessResult(int exitCode, IReadOnlyList<ProcessOutput> output)
{
    public int ExitCode { get; } = exitCode;

    public ProcessOutputCollection Output { get; } = new(output);
}

public sealed record ProcessOutput(ProcessOutputType Type, string Text)
{
    public override string ToString()
    {
        return Type switch
        {
            ProcessOutputType.StandardError => "error: " + Text,
            _ => Text,
        };
    }
}

public sealed class ProcessOutputCollection : IReadOnlyList<ProcessOutput>
{
    private readonly IReadOnlyList<ProcessOutput> _output;

    internal ProcessOutputCollection(IReadOnlyList<ProcessOutput> output)
    {
        _output = output;
    }

    public int Count => _output.Count;

    public ProcessOutput this[int index] => _output[index];

    [MustDisposeResource]
    public IEnumerator<ProcessOutput> GetEnumerator() => _output.GetEnumerator();

    [MustDisposeResource]
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

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

public enum ProcessOutputType
{
    StandardOutput,
    StandardError,
}

#endregion
