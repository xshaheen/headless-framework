// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.IO.Compression;
using AwesomeAssertions;

namespace Headless.Templates.Tests.Integration;

public sealed class HeadlessShopTemplateTests
{
    private static readonly DirectoryInfo _RepoRoot = _FindRepoRoot();

    [Fact]
    public async Task template_package_can_be_packed_installed_and_generated()
    {
        using var temp = TempDirectory.Create();
        var packages = temp.Directory("packages");
        var dotnetHome = temp.Directory("dotnet-home");
        var generated = temp.Directory("generated");

        Directory.CreateDirectory(packages.FullName);
        Directory.CreateDirectory(dotnetHome.FullName);

        await _RunDotNet(
            "pack templates/HeadlessShop/HeadlessShop.csproj --configuration Debug --output "
                + _Quote(packages.FullName)
        );

        var package = packages.EnumerateFiles("Headless.Templates.HeadlessShop.*.nupkg").Single();

        await using (
            var packageStream = new FileStream(
                package.FullName,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan
            )
        )
        using (var archive = new ZipArchive(packageStream, ZipArchiveMode.Read))
        {
            archive
                .Entries.Should()
                .Contain(entry =>
                    entry.FullName.EndsWith("content/.template.config/template.json", StringComparison.Ordinal)
                );
        }

        await _RunDotNet("new install " + _Quote(package.FullName) + " --force", dotnetHome);
        await _RunDotNet(
            "new headless-shop -n TrailStore -o " + _Quote(generated.FullName) + " --HeadlessPackageVersion 9.9.9-test",
            dotnetHome
        );

        generated.File("TrailStore.slnx").Exists.Should().BeTrue();
        generated.File("AGENTS.md").Exists.Should().BeTrue();
        generated.File("README.md").Exists.Should().BeTrue();
        generated.File("compose.yaml").Exists.Should().BeTrue();
        generated.File("docs/recipes/add-command.md").Exists.Should().BeTrue();
        generated.File("TrailStore.Api/TrailStore.Api.csproj").Exists.Should().BeTrue();
        generated.File("TrailStore.Tests.Architecture/TrailStore.Tests.Architecture.csproj").Exists.Should().BeTrue();
        (
            await File.ReadAllTextAsync(
                generated.File("Directory.Packages.props").FullName,
                TestContext.Current.CancellationToken
            )
        )
            .Should()
            .Contain("Version=\"9.9.9-test\"")
            .And.NotContain("Version=\"0.10.1-preview.0.13\"");

        var generatedFiles = generated
            .EnumerateFiles("*", SearchOption.AllDirectories)
            .Where(file => file.Extension is ".cs" or ".csproj" or ".md" or ".json")
            .ToArray();

        var generatedContents = await Task.WhenAll(
            generatedFiles.Select(file => File.ReadAllTextAsync(file.FullName, TestContext.Current.CancellationToken))
        );

        generatedContents
            .Should()
            .OnlyContain(
                content => !content.Contains("HeadlessShop", StringComparison.Ordinal),
                "sourceName replacement should produce a clean generated checkout"
            );
    }

    [Fact]
    public void generated_docs_reference_existing_template_paths()
    {
        var content = _RepoRoot.Directory("templates/HeadlessShop/content");

        content.File("AGENTS.md").Exists.Should().BeTrue();
        content.File("README.md").Exists.Should().BeTrue();
        content.File("compose.yaml").Exists.Should().BeTrue();
        content.File("docs/architecture.md").Exists.Should().BeTrue();
        content.File("docs/validation.md").Exists.Should().BeTrue();
        content.File("docs/recipes/add-command.md").Exists.Should().BeTrue();
        content.File("HeadlessShop.Tests.Architecture/ArchitectureRulesTests.cs").Exists.Should().BeTrue();
        content.File("HeadlessShop.Tests.Integration/ShopSmokeTests.cs").Exists.Should().BeTrue();
    }

    [Fact]
    public async Task validation_script_runs_the_full_generated_gate()
    {
        var script = await File.ReadAllTextAsync(
            _RepoRoot.File("tools/validate-headless-shop-template.sh").FullName,
            TestContext.Current.CancellationToken
        );

        script.Should().Contain("dotnet new install");
        script.Should().Contain("dotnet new headless-shop");
        script.Should().Contain("dotnet restore");
        script.Should().Contain("dotnet build");
        script.Should().Contain("HEADLESS_SHOP_LOCAL_PACKAGE_SOURCE");
        script.Should().Contain("--HeadlessPackageVersion");
        script.Should().Contain("dotnet nuget disable source github.com");
        script.Should().Contain("TrailStore.Tests.Architecture");
        script.Should().Contain("TrailStore.Tests.Integration");
        script.Should().Contain("docs/recipes/add-command.md");
        script.Should().Contain("compose.yaml");
    }

    private static async Task _RunDotNet(string arguments, DirectoryInfo? dotnetHome = null)
    {
        var startInfo = new ProcessStartInfo("dotnet", arguments)
        {
            WorkingDirectory = _RepoRoot.FullName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        if (dotnetHome is not null)
        {
            startInfo.Environment["DOTNET_CLI_HOME"] = dotnetHome.FullName;
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start dotnet.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);
        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        process.ExitCode.Should().Be(0, $"dotnet {arguments} failed.\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");
    }

    private static string _Quote(string value)
    {
        return "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
    }

    private static DirectoryInfo _FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current is not null && !current.EnumerateFiles("headless-framework.slnx").Any())
        {
            current = current.Parent;
        }

        return current ?? throw new InvalidOperationException("Could not find repository root.");
    }
}

internal sealed class TempDirectory : IDisposable
{
    private TempDirectory(string path)
    {
        Root = new(path);
    }

    public DirectoryInfo Root { get; }

    public static TempDirectory Create()
    {
        var path = Path.Combine(Path.GetTempPath(), "headless-shop-template-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(path);

        return new(path);
    }

    public DirectoryInfo Directory(string relativePath)
    {
        return new(Path.Combine(Root.FullName, relativePath));
    }

    public void Dispose()
    {
        if (Root.Exists)
        {
            Root.Delete(recursive: true);
        }
    }
}

internal static class FileSystemInfoExtensions
{
    public static FileInfo File(this DirectoryInfo directory, string relativePath)
    {
        return new(Path.Combine(directory.FullName, relativePath));
    }

    public static DirectoryInfo Directory(this DirectoryInfo directory, string relativePath)
    {
        return new(Path.Combine(directory.FullName, relativePath));
    }
}
