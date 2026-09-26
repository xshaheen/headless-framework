// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.Testing.Tests;

namespace Tests;

public sealed class DocsExamplesCompileTests : TestBase
{
    /// <summary>
    /// Examples that fail on a known guide defect whose fix belongs to another change. Each row is skipped with
    /// its reason; the change that fixes the example deletes its entry, and a stale entry fails the run.
    /// </summary>
    private static readonly Dictionary<string, string> _KnownFailures = new(StringComparer.Ordinal)
    {
        // jobs.md belongs to the Jobs documentation change.
        ["jobs.md:39"] = "UseEntityFramework is a JobsOptionsBuilder member, called here on IServiceCollection",
        ["jobs.md:103"] = "elided bodies; 5-field cron and IServiceProvider job parameter are rejected (HF003, HF009)",
        ["jobs.md:267"] = "[JobFunction] on a class; 5-field cron; IServiceProvider job parameter",
        ["jobs.md:439"] = "ConfigureScheduler is a JobsOptionsBuilder member, called here on the WebApplicationBuilder",
        ["jobs.md:521"] = "ConfigureScheduler is a JobsOptionsBuilder member, called here on the WebApplicationBuilder",
        ["jobs.md:834"] = "[JobFunction] on a class; 5-field cron; IServiceProvider job parameter",
        ["jobs.md:904"] = "assembly-level middleware must target a job in another assembly (HF014)",
        ["jobs.md:944"] = "JsonNamingPolicy used without System.Text.Json",
        ["jobs.md:1033"] = "AddDashboard is a JobsOptionsBuilder member, called here on IServiceCollection",
        ["jobs.md:1051"] = "AddDashboard is a JobsOptionsBuilder member, called here on IServiceCollection",
        ["jobs.md:1105"] = "elided bodies; [JobFunction] on a class; duplicate IServiceProvider methods; 5-field crons",
        ["jobs.md:1169"] =
            "AddConsoleExporter needs OpenTelemetry.Exporter.Console, which the check does not reference",
        ["jobs.md:1278"] = "UseEntityFramework is a JobsOptionsBuilder member, called here on IServiceCollection",
        ["jobs.md:1312"] = "UseEntityFramework called on IServiceCollection; no coordination provider registered",
        ["jobs.md:1371"] = "Polly RetryStrategyOptions used without Polly usings",
        ["jobs.md:1432"] = "[JobFunction] on a class",
        ["jobs.md:1460"] = "Headless.Jobs.Core.Exceptions does not exist; the namespace is Headless.Jobs.Exceptions",
    };

    // Compiling ~450 examples one at a time dominates the run, so every example compiles in parallel once and
    // each theory row reads its own result.
    private static readonly Lazy<ConcurrentDictionary<string, IReadOnlyList<string>>> _Results = new(() =>
    {
        var results = new ConcurrentDictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

        Parallel.ForEach(
            DocsExamples.All.Where(static e => !e.IsFragment),
            example => results[example.Id] = DocsExampleCompiler.Compile(example)
        );

        return results;
    });

    public static IEnumerable<TheoryDataRow<string>> Examples =>
        DocsExamples
            .All.Where(static e => !e.IsFragment)
            .Select(static e => new TheoryDataRow<string>(e.Id) { Skip = _KnownFailures.GetValueOrDefault(e.Id) });

    [Fact]
    public void should_find_examples_in_every_guide()
    {
        var guides = Directory.EnumerateFiles(DocsExamples.GuidesDirectory, "*.md").Select(Path.GetFileName);
        var withExamples = DocsExamples.All.Select(static e => e.Guide).ToHashSet(StringComparer.Ordinal);

        // index.md is a router with no code; every domain guide carries at least one example.
        guides.Where(g => !withExamples.Contains(g!)).Should().BeEquivalentTo(["index.md"]);
    }

    [Fact]
    public void should_only_list_known_failures_that_still_fail()
    {
        _KnownFailures
            .Keys.Should()
            .NotContain(
                static id => !_Results.Value.ContainsKey(id) || _Results.Value[id].Count == 0,
                "an example that compiles, or no longer exists at that line, must leave the known-failure list"
            );
    }

    [Theory]
    [MemberData(nameof(Examples))]
    public void should_compile_example(string id)
    {
        // Joined so the failure lists every diagnostic instead of the first collection item.
        var errors = string.Join('\n', _Results.Value[id]);

        errors.Should().BeEmpty("the example at docs/llms/{0} must compile against the current public API", id);
    }
}
