// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;

namespace Headless;

/// <summary>
/// Convenience helpers for launching an external process from a file name and arguments, consuming its output either
/// as a task that completes when the process exits or as an asynchronous stream of output lines.
/// </summary>
[PublicAPI]
public static class ProcessHelper
{
    /// <summary>
    /// Executes a process asynchronously based on the provided configuration and waits for its completion while supporting cancellation.
    /// </summary>
    /// <param name="fileName">The executable or command to run.</param>
    /// <param name="arguments">The command-line arguments to pass, or <see langword="null"/> for none.</param>
    /// <param name="cancellationToken">A token that, when canceled, terminates the process and ends the wait.</param>
    /// <returns>
    /// A <see cref="Task{TResult}"/> that represents the completion of the process execution.
    /// The result contains a <see cref="ProcessResult"/> with the exit code and captured standard output/error logs.
    /// </returns>
    /// <exception cref="InvalidOperationException">Thrown when the process cannot start.</exception>
    /// <exception cref="OperationCanceledException">Thrown if the operation is canceled via the provided <paramref name="cancellationToken"/>.</exception>
    public static Task<ProcessResult> RunAsTaskAsync(
        string fileName,
        string? arguments,
        CancellationToken cancellationToken = default
    )
    {
        return RunAsTaskAsync(fileName, arguments, workingDirectory: null, cancellationToken);
    }

    /// <inheritdoc cref="RunAsTaskAsync(string,string?,System.Threading.CancellationToken)"/>
    /// <param name="workingDirectory">The working directory for the process, or <see langword="null"/> to inherit the current one.</param>
    public static Task<ProcessResult> RunAsTaskAsync(
        string fileName,
        string? arguments,
        string? workingDirectory,
        CancellationToken cancellationToken = default
    )
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            ErrorDialog = false,
            UseShellExecute = false,
        };

        if (arguments is not null)
        {
            psi.Arguments = arguments;
        }

        if (workingDirectory is not null)
        {
            psi.WorkingDirectory = workingDirectory;
        }

        return psi.RunAsTaskAsync(cancellationToken);
    }

    /// <inheritdoc cref="RunAsTaskAsync(string,string?,System.Threading.CancellationToken)"/>
    /// <param name="workingDirectory">The working directory for the process, or <see langword="null"/> to inherit the current one.</param>
    public static Task<ProcessResult> RunAsTaskAsync(
        string fileName,
        IEnumerable<string>? arguments,
        string? workingDirectory,
        CancellationToken cancellationToken = default
    )
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            ErrorDialog = false,
            UseShellExecute = false,
        };

        if (arguments is not null)
        {
            psi.ArgumentList.AddRange(arguments);
        }

        if (workingDirectory is not null)
        {
            psi.WorkingDirectory = workingDirectory;
        }

        return psi.RunAsTaskAsync(cancellationToken);
    }

    /// <summary>
    /// Runs the specified process and streams its standard output/error lines as they are printed, followed by a
    /// final exit-code item.
    /// </summary>
    /// <param name="fileName">The executable or command to run.</param>
    /// <param name="arguments">The command-line arguments to pass, or <see langword="null"/> for none.</param>
    /// <param name="cancellationToken">A token that, when canceled, terminates the process tree and ends the enumeration.</param>
    /// <returns>
    /// An <see cref="IAsyncEnumerable{T}"/> that starts the process when enumeration begins and yields each standard
    /// output/error line in arrival order, then exactly one <see cref="ProcessStreamedOutputType.ExitCode"/> item.
    /// Ending the enumeration early, by breaking out of the loop or by cancellation, terminates the process tree.
    /// </returns>
    /// <exception cref="InvalidOperationException">Thrown during enumeration when the process cannot start.</exception>
    /// <exception cref="OperationCanceledException">Thrown during enumeration when <paramref name="cancellationToken"/> is canceled.</exception>
    public static IAsyncEnumerable<ProcessStreamedOutput> RunAndStreamAsync(
        string fileName,
        string? arguments,
        CancellationToken cancellationToken = default
    )
    {
        return RunAndStreamAsync(fileName, arguments, workingDirectory: null, cancellationToken);
    }

    /// <inheritdoc cref="RunAndStreamAsync(string,string?,CancellationToken)"/>
    /// <param name="workingDirectory">The working directory for the process, or <see langword="null"/> to inherit the current one.</param>
    public static IAsyncEnumerable<ProcessStreamedOutput> RunAndStreamAsync(
        string fileName,
        string? arguments,
        string? workingDirectory,
        CancellationToken cancellationToken = default
    )
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            ErrorDialog = false,
            UseShellExecute = false,
        };

        if (arguments is not null)
        {
            psi.Arguments = arguments;
        }

        if (workingDirectory is not null)
        {
            psi.WorkingDirectory = workingDirectory;
        }

        return psi.RunAndStreamAsync(cancellationToken);
    }

    /// <inheritdoc cref="RunAndStreamAsync(string,string?,CancellationToken)"/>
    /// <param name="workingDirectory">The working directory for the process, or <see langword="null"/> to inherit the current one.</param>
    public static IAsyncEnumerable<ProcessStreamedOutput> RunAndStreamAsync(
        string fileName,
        IEnumerable<string>? arguments,
        string? workingDirectory,
        CancellationToken cancellationToken = default
    )
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            ErrorDialog = false,
            UseShellExecute = false,
        };

        if (arguments is not null)
        {
            psi.ArgumentList.AddRange(arguments);
        }

        if (workingDirectory is not null)
        {
            psi.WorkingDirectory = workingDirectory;
        }

        return psi.RunAndStreamAsync(cancellationToken);
    }
}
