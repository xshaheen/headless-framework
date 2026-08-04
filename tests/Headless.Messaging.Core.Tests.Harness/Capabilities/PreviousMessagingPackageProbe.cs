// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;

namespace Tests.Capabilities;

public static class PreviousMessagingPackageProbe
{
    private const string _PreviousPackageVersion = "0.11.0";

    public static async Task<PreviousMessagingPackageConsumerProcess> StartConsumerAsync(
        string provider,
        string lane,
        string endpoint,
        string logicalName,
        string group,
        string messageId,
        CancellationToken cancellationToken
    )
    {
        var process = _Start("consume", provider, lane, endpoint, logicalName, group, messageId);
        var consumer = new PreviousMessagingPackageConsumerProcess(process, provider, lane, messageId);

        try
        {
            await consumer.WaitUntilReadyAsync(cancellationToken);
            return consumer;
        }
        catch
        {
            await consumer.DisposeAsync();
            throw;
        }
    }

    public static async Task ProduceAsync(
        string provider,
        string lane,
        string endpoint,
        string logicalName,
        string group,
        string messageId,
        CancellationToken cancellationToken
    )
    {
        using var process = _Start("produce", provider, lane, endpoint, logicalName, group, messageId);
        var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var expected = $"PRODUCED|{provider}|{_PreviousPackageVersion}|{messageId}";

        if (process.ExitCode != 0 || !output.Contains(expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Previous-package producer failed with exit code {process.ExitCode}. Output: {output} Error: {error}"
            );
        }
    }

    private static Process _Start(
        string operation,
        string provider,
        string lane,
        string endpoint,
        string logicalName,
        string group,
        string messageId
    )
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add(_ResolveProbeAssembly());
        startInfo.ArgumentList.Add(operation);
        startInfo.ArgumentList.Add(provider);
        startInfo.ArgumentList.Add(lane);
        startInfo.ArgumentList.Add(endpoint);
        startInfo.ArgumentList.Add(logicalName);
        startInfo.ArgumentList.Add(group);
        startInfo.ArgumentList.Add(messageId);

        return Process.Start(startInfo)
            ?? throw new InvalidOperationException("Unable to start the previous Messaging package probe process.");
    }

    private static string _ResolveProbeAssembly()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        var configuration = directory.Parent?.Name;
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "headless-framework.slnx")))
        {
            directory = directory.Parent;
        }

        if (directory is null || string.IsNullOrWhiteSpace(configuration))
        {
            throw new InvalidOperationException(
                "Unable to resolve the repository root for the previous-package probe."
            );
        }

        var assembly = Path.Combine(
            directory.FullName,
            "tests",
            "Headless.Messaging.PreviousVersionProbe",
            "bin",
            configuration,
            "net10.0",
            "Headless.Messaging.PreviousVersionProbe.dll"
        );

        if (!File.Exists(assembly))
        {
            throw new InvalidOperationException(
                $"Previous Messaging package probe was not built at '{assembly}'. Run make build-messaging-previous-version-probe."
            );
        }

        return assembly;
    }
}

public sealed class PreviousMessagingPackageConsumerProcess : IAsyncDisposable
{
    private const string _PreviousPackageVersion = "0.11.0";
    private readonly string _lane;
    private readonly string _messageId;
    private readonly Process _process;
    private readonly string _provider;
    private int _disposed;

    internal PreviousMessagingPackageConsumerProcess(Process process, string provider, string lane, string messageId)
    {
        _process = process;
        _provider = provider;
        _lane = lane;
        _messageId = messageId;
    }

    public bool HasExited => _process.HasExited;

    public async Task WaitUntilReceivedAsync(CancellationToken cancellationToken)
    {
        var expected = $"RECEIVED|{_provider}|{_PreviousPackageVersion}|{_messageId}";
        await _ExpectProtocolLineAsync(expected, cancellationToken);
    }

    public Task CommitAsync(CancellationToken cancellationToken) =>
        _CompleteAsync("COMMIT", "DRAINED", cancellationToken);

    public Task AbortAsync(CancellationToken cancellationToken) =>
        _CompleteAsync("ABORT", "ABORTED", cancellationToken);

    internal async Task WaitUntilReadyAsync(CancellationToken cancellationToken)
    {
        var expected = $"READY|{_provider}|{_PreviousPackageVersion}|{_lane}";
        await _ExpectProtocolLineAsync(expected, cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return ValueTask.CompletedTask;
        }

        if (!_process.HasExited)
        {
            try
            {
                _process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException) when (_process.HasExited) { }
        }

        _process.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task _CompleteAsync(string command, string expectedState, CancellationToken cancellationToken)
    {
        await _process.StandardInput.WriteLineAsync(command.AsMemory(), cancellationToken);
        await _process.StandardInput.FlushAsync(cancellationToken);
        var expected = $"{expectedState}|{_provider}|{_PreviousPackageVersion}|{_messageId}";
        await _ExpectProtocolLineAsync(expected, cancellationToken);
        await _process.WaitForExitAsync(cancellationToken);

        if (_process.ExitCode != 0)
        {
            var error = await _process.StandardError.ReadToEndAsync(cancellationToken);
            throw new InvalidOperationException(
                $"Previous-package consumer exited with code {_process.ExitCode}. Error: {error}"
            );
        }
    }

    private async Task _ExpectProtocolLineAsync(string expected, CancellationToken cancellationToken)
    {
        var line = await _process.StandardOutput.ReadLineAsync(cancellationToken);
        if (!string.Equals(line, expected, StringComparison.Ordinal))
        {
            var error = _process.HasExited
                ? await _process.StandardError.ReadToEndAsync(cancellationToken)
                : "<process still running>";
            throw new InvalidOperationException(
                $"Expected previous-package probe line '{expected}', received '{line ?? "<eof>"}'. Error: {error}"
            );
        }
    }
}
