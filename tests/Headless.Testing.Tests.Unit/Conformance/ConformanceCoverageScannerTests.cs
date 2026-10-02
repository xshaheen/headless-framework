// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Tests;

namespace Tests.Conformance;

public sealed class ConformanceCoverageScannerTests : TestBase
{
    private const string _Harness = """
        public abstract class WidgetTestsBase : TestBase
        {
            public virtual Task should_round_trip() => Task.CompletedTask;

            public virtual Task should_expire() => Task.CompletedTask;

            [Fact]
            public virtual Task should_always_run() => Task.CompletedTask;

            protected virtual Task HookAsync() => Task.CompletedTask;
        }
        """;

    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("conformance-scan-");

    protected override ValueTask DisposeAsyncCore()
    {
        _root.Delete(recursive: true);
        return base.DisposeAsyncCore();
    }

    [Fact]
    public async Task should_report_a_case_the_provider_does_not_override()
    {
        await _WriteAsync("Widgets.Tests.Harness/WidgetTestsBase.cs", _Harness);
        await _WriteAsync(
            "Widgets.Redis.Tests.Integration/RedisWidgetTests.cs",
            """
            public sealed class RedisWidgetTests : WidgetTestsBase
            {
                [Fact]
                public override Task should_round_trip() => base.should_round_trip();
            }
            """
        );

        var result = await ConformanceCoverageScanner.ScanAsync(_root.FullName, AbortToken);

        result
            .Gaps.Should()
            .ContainSingle()
            .Which.Should()
            .Be(
                new ConformanceGap(
                    "Widgets.Redis.Tests.Integration",
                    "RedisWidgetTests",
                    "should_expire",
                    "WidgetTestsBase"
                )
            );
        result.CaseCountByBase["WidgetTestsBase"].Should().Be(2);
    }

    [Fact]
    public async Task should_report_an_override_without_a_test_attribute_or_with_an_unconditional_skip()
    {
        await _WriteAsync("Widgets.Tests.Harness/WidgetTestsBase.cs", _Harness);
        await _WriteAsync(
            "Widgets.Redis.Tests.Integration/RedisWidgetTests.cs",
            """
            public sealed class RedisWidgetTests : WidgetTestsBase
            {
                public override Task should_round_trip() => base.should_round_trip();

                [Fact(Skip = "flaky")]
                public override Task should_expire() => base.should_expire();
            }
            """
        );

        var result = await ConformanceCoverageScanner.ScanAsync(_root.FullName, AbortToken);

        result.Gaps.Select(g => g.Case).Should().BeEquivalentTo(["should_round_trip", "should_expire"]);
    }

    [Fact]
    public async Task should_accept_overrides_from_an_intermediate_base_and_conditional_skips()
    {
        await _WriteAsync("Widgets.Tests.Harness/WidgetTestsBase.cs", _Harness);
        await _WriteAsync(
            "Widgets.Sql.Tests.Integration/SqlWidgetTests.cs",
            """
            public abstract class SqlWidgetTestsBase : WidgetTestsBase
            {
                [Fact]
                public override Task should_round_trip() => base.should_round_trip();
            }

            public sealed class SqlServerWidgetTests : SqlWidgetTestsBase
            {
                [Theory(Skip = "needs a server", SkipUnless = nameof(HasServer))]
                [InlineData(1)]
                public override Task should_expire() => base.should_expire();
            }
            """
        );

        (await ConformanceCoverageScanner.ScanAsync(_root.FullName, AbortToken)).Gaps.Should().BeEmpty();
    }

    [Fact]
    public async Task should_report_a_harness_base_that_no_provider_derives_from()
    {
        await _WriteAsync("Widgets.Tests.Harness/WidgetTestsBase.cs", _Harness);

        var result = await ConformanceCoverageScanner.ScanAsync(_root.FullName, AbortToken);

        result
            .OrphanBases.Should()
            .ContainSingle()
            .Which.Should()
            .Be(new OrphanHarnessBase("Widgets.Tests.Harness", "WidgetTestsBase", 2));
    }

    [Fact]
    public async Task should_ignore_virtual_hooks_on_harness_classes_that_no_test_class_uses()
    {
        await _WriteAsync(
            "Widgets.Tests.Harness/WidgetDriver.cs",
            """
            public abstract class WidgetDriver
            {
                public virtual void Configure() { }
            }
            """
        );
        await _WriteAsync(
            "Widgets.Redis.Tests.Integration/RedisWidgetDriver.cs",
            "public sealed class RedisWidgetDriver : WidgetDriver { }"
        );

        var result = await ConformanceCoverageScanner.ScanAsync(_root.FullName, AbortToken);

        result.Gaps.Should().BeEmpty();
        result.OrphanBases.Should().BeEmpty();
    }

    private async Task _WriteAsync(string relativePath, string source)
    {
        var path = Path.Combine(_root.FullName, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, source, AbortToken);
    }
}
