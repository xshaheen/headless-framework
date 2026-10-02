// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Tests;

namespace Tests.Conformance;

public sealed class ConformanceCoverageTests : TestBase
{
    [Fact]
    public async Task should_run_every_harness_case_in_every_provider_or_allow_list_it()
    {
        var scan = await _ScanAsync();

        var allowed = ConformanceCaseAllowList.Entries.Select(e => (e.ProviderClass, e.Case)).ToHashSet();

        var unlisted = scan
            .Gaps.Where(g => !allowed.Contains((g.ProviderClass, g.Case)))
            .Select(g =>
                $"{g.Project}/{g.ProviderClass}.{g.Case} (from {g.DeclaringBase}): override it with [Fact] or [Theory], "
                + "or add a ConformanceSkip with the capability reason"
            )
            .Order(StringComparer.Ordinal)
            .ToList();

        unlisted.Should().BeEmpty("a harness case that no override runs is skipped without any report");
    }

    [Fact]
    public async Task should_keep_every_allow_list_entry_matching_a_case_that_does_not_run()
    {
        var scan = await _ScanAsync();

        var gaps = scan.Gaps.Select(g => (g.ProviderClass, g.Case)).ToHashSet();

        var stale = ConformanceCaseAllowList
            .Entries.Where(e => !gaps.Contains((e.ProviderClass, e.Case)))
            .Select(e => $"{e.ProviderClass}.{e.Case}")
            .ToList();

        stale.Should().BeEmpty("an entry for a case that now runs, or no longer exists, hides the next real gap");
    }

    [Fact]
    public void should_give_every_allow_list_entry_a_reason_and_list_it_once()
    {
        ConformanceCaseAllowList
            .Entries.Should()
            .NotContain(
                e => string.IsNullOrWhiteSpace(e.Reason),
                "each skipped case must say which capability the provider lacks"
            );

        ConformanceCaseAllowList.Entries.Should().OnlyHaveUniqueItems(e => e.ProviderClass + "." + e.Case);
    }

    [Fact]
    public async Task should_name_each_provider_class_uniquely_so_allow_list_entries_are_unambiguous()
    {
        var scan = await _ScanAsync();

        scan.CoveredByProviderAndBase.Keys.GroupBy(k => k.ProviderClass, StringComparer.Ordinal)
            .Where(g => g.Select(k => k.Project).Distinct(StringComparer.Ordinal).Skip(1).Any())
            .Select(g => g.Key)
            .Should()
            .BeEmpty("allow-list entries name the provider class without its project");
    }

    [Fact]
    public async Task should_have_a_provider_class_for_every_harness_base_that_declares_cases()
    {
        var scan = await _ScanAsync();

        scan.OrphanBases.Select(o => $"{o.Project}/{o.BaseClass} ({o.CaseCount} cases)")
            .Should()
            .BeEmpty("a harness base no provider derives from never runs any of its cases");
    }

    [Fact]
    public async Task should_find_the_known_harness_bases()
    {
        var scan = await _ScanAsync();

        // Guards the scanner itself: if parsing or base resolution regressed to finding nothing, every other test
        // here would pass vacuously.
        scan.CaseCountByBase.Keys.Should().Contain(["DistributedLockTestsBase", "CacheConformanceTestsBase"]);
        scan.CaseCountByBase["DistributedLockTestsBase"].Should().BeGreaterThan(20);
    }

    private Task<ConformanceScanResult> _ScanAsync()
    {
        return ConformanceCoverageScanner.ScanAsync(Path.Combine(_FindRepositoryRoot(), "tests"), AbortToken);
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
