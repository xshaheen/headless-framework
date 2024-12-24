// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;

namespace Framework.Core;

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
}
