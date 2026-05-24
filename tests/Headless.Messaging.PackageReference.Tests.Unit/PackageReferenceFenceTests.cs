// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Headless.Testing.Tests;

namespace Tests;

public sealed class PackageReferenceFenceTests : TestBase
{
    [Theory]
    [InlineData("QueueOnlyCannotResolveBus", "IBus")]
    [InlineData("BusOnlyCannotResolveQueue", "IQueue")]
    public async Task should_keep_bus_and_queue_abstractions_compile_time_isolated(
        string probeName,
        string missingType
    )
    {
        // given
        var projectPath = Path.Combine(
            _FindRepositoryRoot(),
            "tests",
            "Headless.Messaging.PackageReference.Tests.Unit",
            "Probes",
            probeName,
            $"{probeName}.csproj"
        );

        // when
        var result = await _RunDotnetBuildAsync(projectPath, TestContext.Current.CancellationToken);

        // then
        result.ExitCode.Should().NotBe(0);
        result.Output.Should().Contain("CS0246");
        result.Output.Should().Contain(missingType);
    }

    private static async Task<ProcessResult> _RunDotnetBuildAsync(
        string projectPath,
        CancellationToken cancellationToken
    )
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                ArgumentList =
                {
                    "build",
                    projectPath,
                    "-v:q",
                    "-nologo",
                    "/clp:ErrorsOnly",
                    "-p:NuGetAudit=false",
                },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            },
        };

        process.Start();
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        return new ProcessResult(process.ExitCode, await outputTask + await errorTask);
    }

    private static string _FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "headless-framework.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate repository root from test output directory.");
    }

    private sealed record ProcessResult(int ExitCode, string Output);
}
