// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Dashboard;
using Headless.Testing.Tests;

namespace Tests.Modules;

/// <summary>
/// The shell refuses a module by comparing its manifest's SDK version with <see cref="ManifestMerger.ShellSdkVersion"/>,
/// and modules stamp the version from the SDK package, so the two must move together.
/// </summary>
public sealed class ShellSdkVersionTests : TestBase
{
    [Fact]
    public void should_match_the_sdk_package_version_when_the_shell_declares_its_sdk_version()
    {
        var packageJson = Path.Combine(
            _FindRepositoryRoot(),
            "src",
            "Headless.Dashboard",
            "wwwroot",
            "packages",
            "sdk",
            "package.json"
        );
        using var document = JsonDocument.Parse(File.ReadAllText(packageJson));

        var packageVersion = document.RootElement.GetProperty("version").GetString();

        ManifestMerger.ShellSdkVersion.ToString(3).Should().Be(packageVersion);
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
