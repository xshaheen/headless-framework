// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Headless.Testing.Tests;

namespace Tests.Core;

public sealed class ProcessExtensionsTests : TestBase
{
    [Fact]
    public async Task run_and_stream_yields_output_and_error_lines_then_one_exit_code()
    {
        // given
        var psi = _Shell("echo out; echo err 1>&2; exit 3");

        // when
        var items = new List<ProcessStreamedOutput>();

        await foreach (var item in psi.RunAndStreamAsync(AbortToken))
        {
            items.Add(item);
        }

        // then
        items.Should().Contain(new ProcessStreamedOutput(ProcessStreamedOutputType.StandardOutput, "out"));
        items.Should().Contain(new ProcessStreamedOutput(ProcessStreamedOutputType.StandardError, "err"));
        items.Should().ContainSingle(item => item.Type == ProcessStreamedOutputType.ExitCode);
        items[^1].Should().Be(new ProcessStreamedOutput(ProcessStreamedOutputType.ExitCode, "3"));
    }

    [Fact]
    public async Task run_and_stream_kills_the_process_when_the_consumer_stops_early()
    {
        // given: a process that would outlive the test if the enumerator did not kill it
        var psi = _Shell("echo ready; sleep 60");
        var stopwatch = Stopwatch.StartNew();

        // when
        ProcessStreamedOutput? first = null;

        await foreach (var item in psi.RunAndStreamAsync(AbortToken))
        {
            first = item;

            break;
        }

        // then: disposal waits for exit, so returning well before the sleep ends proves the kill
        first.Should().Be(new ProcessStreamedOutput(ProcessStreamedOutputType.StandardOutput, "ready"));
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task run_and_stream_throws_and_kills_the_process_when_canceled()
    {
        // given
        var psi = _Shell("echo ready; sleep 60");
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);
        var stopwatch = Stopwatch.StartNew();

        // when
        var act = async () =>
        {
            await foreach (var _ in psi.RunAndStreamAsync(cts.Token))
            {
                await cts.CancelAsync();
            }
        };

        // then
        await act.Should().ThrowAsync<OperationCanceledException>();
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(30));
    }

    private static ProcessStartInfo _Shell(string script)
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "The scripts use POSIX sh.");

        var psi = new ProcessStartInfo
        {
            FileName = "/bin/sh",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add(script);

        return psi;
    }
}
