// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Headless.Testing.Tests;

namespace Tests;

public sealed class PackageReferenceFenceTests : TestBase
{
    [Theory]
    [InlineData("QueueOnlyCannotResolveBus", "IBus")]
    [InlineData("BusOnlyCannotResolveQueue", "IQueue")]
    public async Task should_keep_bus_and_queue_abstractions_compile_time_isolated(string probeName, string missingType)
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
        var result = await _RunDotnetBuildAsync(projectPath, AbortToken);

        // then
        result.ExitCode.Should().NotBe(0);
        var output = result.Output.ToString();
        output.Should().Contain("CS0246");
        output.Should().Contain(missingType);
    }

    private static async Task<ProcessResult> _RunDotnetBuildAsync(
        string projectPath,
        CancellationToken cancellationToken
    )
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            ArgumentList = { "build", projectPath, "-v:q", "-nologo", "/clp:ErrorsOnly", "-p:NuGetAudit=false" },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        return await startInfo.RunAsTaskAsync(cancellationToken);
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
}
