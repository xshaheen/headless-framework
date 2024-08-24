using System.ComponentModel;
using System.Diagnostics;
using System.Reactive.Linq;

namespace Framework.BuildingBlocks.Extensions.System;

public static partial class ProcessExtensions
{
    public static IObservable<ProcessObservedOutput> RunAsObservable(string fileName, string? arguments)
    {
        return RunAsObservable(fileName, arguments, workingDirectory: null);
    }

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

        return RunAsObservable(psi);
    }

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

        return RunAsObservable(psi);
    }

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
                    observer.OnError(new Win32Exception("Cannot start the process"));

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

                    await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    await registration.DisposeAsync().ConfigureAwait(false);
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
}

#region Types

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
    ExitCode
}

#endregion
