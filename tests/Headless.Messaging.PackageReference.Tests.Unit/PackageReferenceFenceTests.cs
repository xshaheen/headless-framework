// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Xml.Linq;
using Headless.Testing.Tests;

namespace Tests;

public sealed class PackageReferenceFenceTests : TestBase
{
    [Fact]
    public void should_pin_complete_previous_messaging_family()
    {
        var references = _ReadPackageReferences("PreviousAllOld", "PreviousAllOld.csproj");

        references.Should().HaveCount(19);
        references.Select(reference => reference.Version).Should().AllBeEquivalentTo("0.11.0");
        references.Should().OnlyHaveUniqueItems(reference => reference.Id);
    }

    [Fact]
    public void should_cover_complete_current_messaging_family()
    {
        var root = _FindRepositoryRoot();
        var expected = File.ReadAllLines(Path.Combine(root, "eng", "expected-packages.txt"))
            .Where(package => package.StartsWith("Headless.Messaging.", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();
        var references = _ReadPackageReferences("NewAllNew", "NewAllNew.csproj");

        references.Select(reference => reference.Id).Order(StringComparer.Ordinal).Should().Equal(expected);
        references.Select(reference => reference.Version).Should().AllBeEquivalentTo("$(MessagingPackageVersion)");
    }

    [Fact]
    public void should_keep_selected_mixed_probe_narrow()
    {
        var references = _ReadPackageReferences("SelectedMixed", "SelectedMixed.csproj");

        references
            .Should()
            .BeEquivalentTo([
                new PackageReference("Headless.Messaging.Core", "0.11.0"),
                new PackageReference("Headless.Messaging.Redis", "$(MessagingPackageVersion)"),
            ]);
    }

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

    private static IReadOnlyList<PackageReference> _ReadPackageReferences(string probeName, string projectName)
    {
        var path = Path.Combine(
            _FindRepositoryRoot(),
            "tests",
            "Headless.Messaging.PackageReference.Tests.Unit",
            "Probes",
            "Compatibility",
            probeName,
            projectName
        );
        var project = XDocument.Load(path);

        return project
            .Descendants("PackageReference")
            .Select(element => new PackageReference(
                element.Attribute("Include")?.Value
                    ?? throw new InvalidOperationException($"PackageReference in {path} has no Include."),
                element.Attribute("Version")?.Value
                    ?? throw new InvalidOperationException($"PackageReference in {path} has no Version.")
            ))
            .ToArray();
    }

    private sealed record PackageReference(string Id, string Version);
}
