// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;

namespace Headless.Core;

[PublicAPI]
public static class ProcessHelper
{
    /// <summary>
    /// Executes a process asynchronously based on the provided configuration and waits for its completion while supporting cancellation.
    /// </summary>
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
    /// Converts an asynchronous operation or task into an observable sequence,
    /// allowing for reactive-style usage and composition of the resulting data or notifications.
    /// </summary>
    /// <returns>
    /// An IObservable sequence that represents the completion or result of the asynchronous operation.
    /// OnNext: It returns the process standard output/error as it printed.
    /// OnError: It returns <see cref="InvalidOperationException"/> when cannot start the process.
    /// </returns>
    public static IObservable<ProcessObservedOutput> RunAsObservable(string fileName, string? arguments)
    {
        return RunAsObservable(fileName, arguments, workingDirectory: null);
    }

    /// <inheritdoc cref="RunAsObservable(string,string?)"/>
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
