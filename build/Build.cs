// Copyright (c) Mahmoud Shaheen. All rights reserved.

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

[PublicAPI]
file sealed class Build : NukeBuild
{
    public static int Main() => Execute<Build>(x => x.Compile);

    static readonly AbsolutePath SourceDirectory = RootDirectory / "src";
    static readonly AbsolutePath TestsDirectory = RootDirectory / "tests";
    static readonly AbsolutePath OutputDirectory = RootDirectory / "artifacts";
    static readonly AbsolutePath TestResults = OutputDirectory / "test-results";
    static readonly AbsolutePath PackagesResults = OutputDirectory / "packages-results";

    [Solution]
    readonly Solution Solution = null!;

    [Parameter("Configuration to build - Default is 'Debug' (local) or 'Release' (server)")]
    readonly Configuration Configuration = IsLocalBuild ? Configuration.Debug : Configuration.Release;

    [Parameter("Test project path (relative to tests directory) for coverage analysis")]
    readonly string TestProject = string.Empty;

    [Parameter("Minimum line coverage threshold (default: 80)")]
    readonly int CoverageLineThreshold = 80;

    [Parameter("Minimum branch coverage threshold (default: 80)")]
    readonly int CoverageBranchThreshold = 80;

    [Parameter("Output coverage results as JSON for agent consumption")]
    readonly bool CoverageJsonOutput = false;

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
                        .AllProjects.Where(x => x.Name.Contains(".Tests.", StringComparison.Ordinal))
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

    Target CoverageAnalysis =>
        x =>
            x.Description("Run code coverage analysis with Coverlet and generate HTML report")
                .DependsOn(Compile)
                .Executes(() =>
                {
                    var testProjectPath = string.IsNullOrEmpty(TestProject)
                        ? throw new Exception("TestProject parameter required. Example: --test-project Framework.Checks.Tests.Unit")
                        : TestsDirectory / TestProject / $"{TestProject}.csproj";

                    if (!File.Exists(testProjectPath))
                    {
                        throw new Exception($"Test project not found: {testProjectPath}");
                    }

                    var coverageResultsDir = TestsDirectory / TestProject / "TestResults";
                    var coverageHtmlDir = RootDirectory / "coverage" / "html";

                    // Run tests with Coverlet coverage collection
                    DotNetTest(settings => settings
                        .SetProjectFile(testProjectPath)
                        .SetConfiguration(Configuration)
                        .SetDataCollector("XPlat Code Coverage")
                        .SetResultsDirectory(coverageResultsDir)
                        .SetProcessEnvironmentVariable("CollectCoverage", "true")
                        .SetProcessEnvironmentVariable("CoverletOutputFormat", "cobertura")
                        .SetProcessEnvironmentVariable("Threshold", CoverageLineThreshold.ToString())
                        .SetProcessEnvironmentVariable("ThresholdType", "line,branch")
                        .SetProcessEnvironmentVariable("ThresholdStat", "total")
                        .SetProcessEnvironmentVariable("ExcludeByAttribute", "CompilerGenerated,GeneratedCode,ExcludeFromCodeCoverage")
                    );

                    // Find the coverage file
                    var coverageFile = coverageResultsDir.GlobFiles("**/coverage.cobertura.xml").FirstOrDefault();
                    if (coverageFile == null)
                    {
                        throw new Exception($"Coverage file not found in {coverageResultsDir}");
                    }

                    // Generate HTML report using reportgenerator
                    ProcessTasks.StartProcess(
                        "reportgenerator",
                        $"-reports:\"{coverageFile}\" -targetdir:\"{coverageHtmlDir}\" -reporttypes:\"Html;TextSummary\"",
                        workingDirectory: RootDirectory
                    ).AssertWaitForExit();

                    // Parse and display summary
                    var summaryFile = coverageHtmlDir / "Summary.txt";
                    if (!File.Exists(summaryFile))
                    {
                        throw new Exception($"Summary file not found: {summaryFile}");
                    }

                    var summaryText = File.ReadAllText(summaryFile);
                    var coverageData = ParseCoverageSummary(summaryText);

                    if (CoverageJsonOutput)
                    {
                        // Output JSON for agent consumption
                        var jsonOutput = System.Text.Json.JsonSerializer.Serialize(new
                        {
                            success = true,
                            timestamp = DateTime.UtcNow,
                            testProject = TestProject,
                            coverage = new
                            {
                                line = new
                                {
                                    percentage = coverageData.LinePercentage,
                                    covered = coverageData.CoveredLines,
                                    coverable = coverageData.CoverableLines
                                },
                                branch = new
                                {
                                    percentage = coverageData.BranchPercentage,
                                    covered = coverageData.CoveredBranches,
                                    total = coverageData.TotalBranches
                                }
                            },
                            thresholds = new
                            {
                                line = CoverageLineThreshold,
                                branch = CoverageBranchThreshold
                            },
                            meetsThresholds = coverageData.LinePercentage >= CoverageLineThreshold &&
                                            coverageData.BranchPercentage >= CoverageBranchThreshold,
                            reports = new
                            {
                                html = (coverageHtmlDir / "index.html").ToString(),
                                summary = summaryFile.ToString(),
                                cobertura = coverageFile.ToString()
                            }
                        }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

                        Console.WriteLine(jsonOutput);
                    }
                    else
                    {
                        // Human-readable output
                        Serilog.Log.Information("Coverage Summary:");
                        Serilog.Log.Information(summaryText);
                        Serilog.Log.Information($"HTML report: {coverageHtmlDir / "index.html"}");
                        Serilog.Log.Information($"Thresholds: Line={CoverageLineThreshold}%, Branch={CoverageBranchThreshold}%");
                        Serilog.Log.Information($"Status: {(coverageData.LinePercentage >= CoverageLineThreshold && coverageData.BranchPercentage >= CoverageBranchThreshold ? "PASS" : "FAIL")}");
                    }
                });

    static (double LinePercentage, double BranchPercentage, int CoveredLines, int CoverableLines, int CoveredBranches, int TotalBranches) ParseCoverageSummary(string summaryText)
    {
        double linePercentage = 0;
        double branchPercentage = 0;
        int coveredLines = 0;
        int coverableLines = 0;
        int coveredBranches = 0;
        int totalBranches = 0;

        using var reader = new StringReader(summaryText);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            var trimmed = line.Trim();

            if (trimmed.StartsWith("Line coverage:"))
            {
                var match = System.Text.RegularExpressions.Regex.Match(trimmed, @"(\d+\.?\d*)%");
                if (match.Success)
                {
                    linePercentage = double.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                }
            }
            else if (trimmed.StartsWith("Branch coverage:"))
            {
                var match = System.Text.RegularExpressions.Regex.Match(trimmed, @"(\d+\.?\d*)%");
                if (match.Success)
                {
                    branchPercentage = double.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                }

                // Extract branch counts: "Branch coverage: 81.1% (383 of 472)"
                var countsMatch = System.Text.RegularExpressions.Regex.Match(trimmed, @"\((\d+) of (\d+)\)");
                if (countsMatch.Success)
                {
                    coveredBranches = int.Parse(countsMatch.Groups[1].Value);
                    totalBranches = int.Parse(countsMatch.Groups[2].Value);
                }
            }
            else if (trimmed.StartsWith("Covered lines:"))
            {
                var match = System.Text.RegularExpressions.Regex.Match(trimmed, @"(\d+)");
                if (match.Success)
                {
                    coveredLines = int.Parse(match.Groups[1].Value);
                }
            }
            else if (trimmed.StartsWith("Coverable lines:"))
            {
                var match = System.Text.RegularExpressions.Regex.Match(trimmed, @"(\d+)");
                if (match.Success)
                {
                    coverableLines = int.Parse(match.Groups[1].Value);
                }
            }
        }

        return (linePercentage, branchPercentage, coveredLines, coverableLines, coveredBranches, totalBranches);
    }

    #endregion

    #region Pack

    Target Pack =>
        x =>
            x.Description("Creates NuGet packages and outputs them to the artifacts directory.")
                .Executes(() =>
                {
                    DotNetPack(settings =>
                        settings
                            .SetProject(Solution)
                            .SetConfiguration(Configuration)
                            .EnableNoBuild()
                            .EnableNoRestore()
                            .EnableIncludeSymbols()
                            .SetOutputDirectory(PackagesResults)
                            .SetContinuousIntegrationBuild(!IsLocalBuild)
                    );
                });

    #endregion

    #region Push to Github Packages

    [Parameter("Configuration to build - Default is 'Debug' (local) or 'Release' (server)")]
    readonly string GithubToken = Environment.GetEnvironmentVariable("GITHUB_TOKEN")!;

    Target PushPackages =>
        x =>
            x.Description("Pushes NuGet packages to Github Packages.")
                .DependsOn(Pack)
                .Executes(() =>
                {
                    DotNetNuGetPush(settings =>
                        settings
                            .SetSource("GitHub")
                            .SetApiKey(GithubToken)
                            .EnableSkipDuplicate()
                            .SetTargetPath(PackagesResults / "*.nupkg")
                    );

                    // Push symbols
                    DotNetNuGetPush(settings =>
                        settings
                            .SetSource("GitHub")
                            .SetApiKey(GithubToken)
                            .EnableSkipDuplicate()
                            .SetTargetPath(PackagesResults / "*.snupkg")
                    );
                });

    #endregion
}
