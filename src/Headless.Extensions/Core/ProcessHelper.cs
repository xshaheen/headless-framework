// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;

namespace Headless.Core;

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
    /// Runs the specified process and exposes its standard output/error and exit code as an observable sequence,
    /// allowing reactive-style consumption of the streamed lines.
    /// </summary>
    /// <param name="fileName">The executable or command to run.</param>
    /// <param name="arguments">The command-line arguments to pass, or <see langword="null"/> for none.</param>
    /// <returns>
    /// An <see cref="IObservable{T}"/> that streams the process output.
    /// OnNext: each standard output/error line as it is printed, then a final exit-code item.
    /// OnError: an <see cref="InvalidOperationException"/> when the process cannot be started.
    /// </returns>
    public static IObservable<ProcessObservedOutput> RunAsObservable(string fileName, string? arguments)
    {
        return RunAsObservable(fileName, arguments, workingDirectory: null);
    }

    /// <inheritdoc cref="RunAsObservable(string,string?)"/>
    /// <param name="workingDirectory">The working directory for the process, or <see langword="null"/> to inherit the current one.</param>
    public static IObservable<ProcessObservedOutput> RunAsObservable(
        string fileName,
        string? arguments,
        string? workingDirectory
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

        return psi.RunAsObservable();
    }

    /// <inheritdoc cref="RunAsObservable(string,string?)"/>
    /// <param name="workingDirectory">The working directory for the process, or <see langword="null"/> to inherit the current one.</param>
    public static IObservable<ProcessObservedOutput> RunAsObservable(
        string fileName,
        IEnumerable<string>? arguments,
        string? workingDirectory
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

        return psi.RunAsObservable();
    }
}
