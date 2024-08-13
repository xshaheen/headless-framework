using Nuke.Common;
using Nuke.Common.IO;
using Nuke.Common.ProjectModel;
using Nuke.Common.Tooling;
using Nuke.Common.Tools.Coverlet;
using Nuke.Common.Tools.DotCover;
using Nuke.Common.Tools.DotNet;
using Nuke.Common.Utilities.Collections;
using static Nuke.Common.Tools.DotCover.DotCoverTasks;
using static Nuke.Common.Tools.DotNet.DotNetTasks;

file sealed class Build : NukeBuild
{
    public static int Main() => Execute<Build>(x => x.Compile);

    [Solution]
    readonly Solution Solution = default!;

    [Parameter("Configuration to build - Default is 'Debug' (local) or 'Release' (server)")]
    readonly Configuration Configuration = IsLocalBuild ? Configuration.Debug : Configuration.Release;

    static readonly AbsolutePath SourceDirectory = RootDirectory / "src";
    static readonly AbsolutePath TestsDirectory = RootDirectory / "tests";
    static readonly AbsolutePath OutputDirectory = RootDirectory / "artifacts";
    static readonly AbsolutePath TestResults = OutputDirectory / "test-results";
    static readonly AbsolutePath PackagesResults = OutputDirectory / "packages-results";

    #region Build

    Target Clean =>
        x =>
            x.Description("Cleans the artifacts, bin and obj directories.")
                .Before(Restore)
                .Executes(() =>
                {
                    OutputDirectory.CreateOrCleanDirectory();
                    SourceDirectory.GlobDirectories("**/bin", "**/obj").ForEach(x => x.DeleteDirectory());
                    TestsDirectory.GlobDirectories("**/bin", "**/obj").ForEach(x => x.DeleteDirectory());
                });

    Target Restore =>
        x =>
            x.Description("Restores NuGet packages.")
                .DependsOn(Clean)
                .Executes(() =>
                {
                    DotNetRestore(x =>
                        x.SetProjectFile(Solution).SetProperty("configuration", Configuration.ToString())
                    );
                });

    Target Compile =>
        x =>
            x.Description("Builds the solution.")
                .DependsOn(Restore)
                .Executes(() =>
                {
                    DotNetBuild(settings =>
                        settings.SetProjectFile(Solution).SetConfiguration(Configuration).EnableNoRestore()
                    );
                });

    #endregion

    #region Test

    Target Test =>
        x =>
            x.Description("Runs unit tests and outputs test results to the artifacts directory.")
                .DependsOn(Compile)
                .Executes(() =>
                {
                    Solution
                        .AllProjects.Where(x => x.Name.Contains("Tests", StringComparison.Ordinal))
                        .ForEach(project =>
                        {
                            DotNetTest(settings =>
                                settings
                                    .SetProjectFile(project)
                                    .SetConfiguration(Configuration)
                                    .EnableNoRestore()
                                    .EnableNoBuild()
                                    .EnableBlameCrash()
                                    .SetDataCollector("XPlat Code Coverage")
                                    .EnableCollectCoverage()
                                    .SetResultsDirectory(TestResults)
                                    .SetLoggers(
                                        "console;verbosity=detailed",
                                        $"trx;LogFileName={project.Name}.trx",
                                        $"html;LogFileName={project.Name}.html"
                                    )
                            );
                        });
                });

    Target TestCoverage =>
        x =>
            x.DependsOn(Compile)
                .Executes(() =>
                {
                    DotCoverCover(
                        _ =>
                            OutputDirectory
                                .GlobFiles("*.Tests.*.dll")
                                .Select(testAssembly =>
                                    new DotCoverCoverSettings()
                                        .SetTargetExecutable(ToolPathResolver.GetPathExecutable("dotnet"))
                                        .SetTargetWorkingDirectory(OutputDirectory)
                                        .SetTargetArguments(
                                            $"test --test-adapter-path:. {testAssembly}  --logger trx;LogFileName={testAssembly}_TestResults.xml"
                                        )
                                        .SetFilters("+:MyProject")
                                        .SetAttributeFilters(
                                            "System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute",
                                            "System.CodeDom.Compiler.GeneratedCodeAttribute"
                                        )
                                        .SetOutputFile(GetDotCoverOutputFile(testAssembly))
                                ),
                        Environment.ProcessorCount
                    );
                });

    static AbsolutePath GetDotCoverOutputFile(string testAssembly) =>
        OutputDirectory / $"dotCover_{Path.GetFileName(testAssembly)}.dcvr";

    #endregion

    #region Pack

    Target Pack =>
        x =>
            x.Description("Creates NuGet packages and outputs them to the artifacts directory.")
                .DependsOn(Test)
                .Executes(() =>
                {
                    DotNetPack(settings =>
                        settings
                            .SetProject(Solution)
                            .SetConfiguration(Configuration)
                            .EnableNoBuild()
                            .EnableNoRestore()
                            // Disable because the source generator failed to pack if this is enabled
                            // .EnableIncludeSymbols()
                            .SetOutputDirectory(PackagesResults)
                            .SetContinuousIntegrationBuild(!IsLocalBuild)
                    );
                });

    #endregion
}
