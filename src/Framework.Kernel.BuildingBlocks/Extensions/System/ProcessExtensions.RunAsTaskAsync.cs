using System.Collections;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace Framework.Kernel.BuildingBlocks.Extensions.System;

[PublicAPI]
public static partial class ProcessExtensions
{
    public static Task<ProcessResult> RunAsTaskAsync(
        string fileName,
        string? arguments,
        CancellationToken cancellationToken = default
    )
    {
        return RunAsTaskAsync(fileName, arguments, workingDirectory: null, cancellationToken);
    }

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

        return RunAsTaskAsync(psi, cancellationToken);
    }

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

        return RunAsTaskAsync(psi, cancellationToken);
    }

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
                throw new Win32Exception("Cannot start the process");
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

                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                await registration.DisposeAsync().ConfigureAwait(false);
            }

            exitCode = process.ExitCode;
        }

        cancellationToken.ThrowIfCancellationRequested();

        return new ProcessResult(exitCode, logs);
    }

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

#region Types

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
    StandardError
}

#endregion
