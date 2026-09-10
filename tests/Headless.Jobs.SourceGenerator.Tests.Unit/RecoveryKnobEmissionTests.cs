// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.CodeAnalysis;

namespace Tests;

/// <summary>
/// The generator is the only route from a <c>[JobFunction]</c> attribute to runtime, so a knob it does not emit does
/// not exist however carefully the rest of the chain carries it. These assert the emitted registration text directly
/// rather than through a snapshot, because the distinction that matters — omitted versus explicitly written — is a
/// single clause that a large snapshot makes easy to miss in review.
/// </summary>
public sealed class RecoveryKnobEmissionTests
{
    private const string _Prelude = """
        using System.Threading;
        using System.Threading.Tasks;
        using Headless.Jobs.Base;
        using Headless.Jobs.Enums;

        namespace Demo;

        public sealed class Jobs
        {
        """;

    [Fact]
    public void should_emit_nothing_when_the_attribute_leaves_the_recovery_knobs_unset()
    {
        var generated = _Generate(
            $$"""
            {{_Prelude}}
                [JobFunction("unset", "0 * * * * *")]
                public Task RunAsync(CancellationToken cancellationToken) => Task.CompletedTask;
            }
            """
        );

        generated
            .Should()
            .NotContain(
                "OnMissedRun",
                "an unset knob must fall through to the scheduler-wide default at creation; emitting a value would "
                    + "pin every definition to the framework default and make that setting unreachable"
            );
        generated.Should().NotContain("MissedRunGraceSeconds");
    }

    [Fact]
    public void should_emit_the_policy_when_the_attribute_sets_it()
    {
        var generated = _Generate(
            $$"""
            {{_Prelude}}
                [JobFunction("skip", "0 * * * * *", OnMissedRun = MissedRunPolicy.Skip)]
                public Task RunAsync(CancellationToken cancellationToken) => Task.CompletedTask;
            }
            """
        );

        // Skip is ordinal 1; the generator emits the numeric value cast back to the enum.
        generated.Should().Contain("OnMissedRun = (MissedRunPolicy)1");
        generated.Should().NotContain("MissedRunGraceSeconds", "only the knob that was written is emitted");
    }

    [Fact]
    public void should_emit_the_grace_when_the_attribute_sets_it()
    {
        var generated = _Generate(
            $$"""
            {{_Prelude}}
                [JobFunction("grace", "0 * * * * *", MissedRunGraceSeconds = 300)]
                public Task RunAsync(CancellationToken cancellationToken) => Task.CompletedTask;
            }
            """
        );

        generated.Should().Contain("MissedRunGraceSeconds = 300");
        generated.Should().NotContain("OnMissedRun");
    }

    [Fact]
    public void should_emit_both_knobs_together()
    {
        var generated = _Generate(
            $$"""
            {{_Prelude}}
                [JobFunction("both", "0 * * * * *", OnMissedRun = MissedRunPolicy.Skip, MissedRunGraceSeconds = 120)]
                public Task RunAsync(CancellationToken cancellationToken) => Task.CompletedTask;
            }
            """
        );

        generated.Should().Contain("OnMissedRun = (MissedRunPolicy)1");
        generated.Should().Contain("MissedRunGraceSeconds = 120");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void should_report_undefined_policy_and_non_positive_grace_before_emission(int graceSeconds)
    {
        var driver = GeneratorTestHelper.Run(
            $$"""
            {{_Prelude}}
                [JobFunction(
                    "invalid",
                    "0 * * * * *",
                    OnMissedRun = (MissedRunPolicy)999,
                    MissedRunGraceSeconds = {{graceSeconds}}
                )]
                public Task RunAsync(CancellationToken cancellationToken) => Task.CompletedTask;
            }
            """
        );

        var diagnostics = driver.GetRunResult().Diagnostics;
        diagnostics.Should().Contain(x => x.Id == "HF020" && x.Severity == DiagnosticSeverity.Error);
        diagnostics.Should().Contain(x => x.Id == "HF021" && x.Severity == DiagnosticSeverity.Error);
    }

    private static string _Generate(string source)
    {
        var driver = GeneratorTestHelper.Run(source, out var diagnostics);

        diagnostics.Should().NotContain(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        return string.Join("\n", driver.GetRunResult().GeneratedTrees.Select(tree => tree.GetText().ToString()));
    }
}
