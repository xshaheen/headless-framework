// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.CodeAnalysis;

namespace Tests;

/// <summary>
/// The generator is the only route from <c>[Job(FailurePolicy = ...)]</c> to the runtime, and it must hand over a
/// factory rather than the type: Jobs packages are trimming-safe, so the runtime never constructs a policy by
/// reflection. A type the factory cannot construct fails the build here instead of at the first failed run.
/// </summary>
public sealed class FailurePolicyEmissionTests
{
    private const string _Prelude = """
        using System.Threading;
        using System.Threading.Tasks;
        using Headless.Jobs;
        using Headless.Reliability;

        namespace Billing;

        """;

    private const string _JobClass = """
        public sealed class CloseDay : IJob
        {
            public ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken) => default;
        }
        """;

    private const string _ValidPolicy = """
        internal sealed class PaymentsPolicy : FailurePolicy
        {
            protected override void Configure(FailurePolicyBuilder policy) => policy.Immediate(retries: 2);
        }

        """;

    [Fact]
    public void should_emit_a_factory_for_a_declared_failure_policy_that_compiles()
    {
        // given
        var source = $$"""
            {{_Prelude}}
            {{_ValidPolicy}}
                [Job("billing.close-day", FailurePolicy = typeof(PaymentsPolicy))]
            {{_JobClass}}
            """;

        // when
        var generated = _Generate(source);

        // then
        generated.Should().Contain(", FailurePolicy = static () => new global::Billing.PaymentsPolicy() });");
    }

    [Fact]
    public void should_emit_a_factory_for_a_closed_generic_failure_policy()
    {
        // given
        var source = $$"""
            {{_Prelude}}
            public sealed class TieredPolicy<TMarker> : FailurePolicy
            {
                protected override void Configure(FailurePolicyBuilder policy) => policy.Immediate(retries: 1);
            }

            public sealed class Ledger;

                [Job("billing.close-day", FailurePolicy = typeof(TieredPolicy<Ledger>))]
            {{_JobClass}}
            """;

        // when
        var generated = _Generate(source);

        // then
        generated
            .Should()
            .Contain("FailurePolicy = static () => new global::Billing.TieredPolicy<global::Billing.Ledger>()");
    }

    [Fact]
    public void should_emit_no_factory_when_the_job_declares_no_failure_policy()
    {
        // given
        var source = $$"""
            {{_Prelude}}
                [Job("billing.close-day")]
            {{_JobClass}}
            """;

        // when
        var generated = _Generate(source);

        // then
        generated
            .Should()
            .NotContain(
                "FailurePolicy",
                "a job without its own policy must fall through to the host default resolved at startup"
            );
    }

    [Theory]
    [InlineData("public sealed class Policy { }", "typeof(Policy)")]
    [InlineData("public sealed class Policy { }", "typeof(Policy[])")]
    [InlineData(
        "public abstract class Policy : FailurePolicy { protected override void Configure(FailurePolicyBuilder policy) { } }",
        "typeof(Policy)"
    )]
    [InlineData(
        "public sealed class Policy<T> : FailurePolicy { protected override void Configure(FailurePolicyBuilder policy) { } }",
        "typeof(Policy<>)"
    )]
    [InlineData(
        "public sealed class Policy : FailurePolicy { private Policy() { } protected override void Configure(FailurePolicyBuilder policy) { } }",
        "typeof(Policy)"
    )]
    [InlineData(
        "public sealed class Policy : FailurePolicy { public Policy(int retries) { } protected override void Configure(FailurePolicyBuilder policy) { } }",
        "typeof(Policy)"
    )]
    [InlineData(
        "file sealed class Policy : FailurePolicy { protected override void Configure(FailurePolicyBuilder policy) { } }",
        "typeof(Policy)"
    )]
    public void should_fail_a_failure_policy_type_the_generated_factory_cannot_construct(
        string declaration,
        string policy
    )
    {
        // given
        var source = $$"""
            {{_Prelude}}
            {{declaration}}

                [Job("billing.close-day", FailurePolicy = {{policy}})]
            {{_JobClass}}
            """;

        // when
        var driver = GeneratorTestHelper.Run(source);

        // then
        var diagnostic = driver.GetRunResult().Diagnostics.Should().ContainSingle(x => x.Id == "HF023").Which;
        diagnostic.Severity.Should().Be(DiagnosticSeverity.Error);
        diagnostic.GetMessage(CultureInfo.InvariantCulture).Should().Contain("Policy").And.Contain("CloseDay");
        driver.GetRunResult().GeneratedTrees.Should().BeEmpty();
    }

    [Fact]
    public void should_fail_a_failure_policy_nested_privately_in_the_job_class()
    {
        // given: the attribute binds inside the job class, so it may name a private nested type the generated module
        // in another class cannot.
        var source = $$"""
            {{_Prelude}}
                [Job("billing.close-day", FailurePolicy = typeof(Policy))]
                public sealed class CloseDay : IJob
                {
                    public ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken) => default;

                    private sealed class Policy : FailurePolicy
                    {
                        protected override void Configure(FailurePolicyBuilder policy) { }
                    }
                }
            """;

        // when
        var driver = GeneratorTestHelper.Run(source);

        // then
        driver
            .GetRunResult()
            .Diagnostics.Should()
            .ContainSingle(x => x.Id == "HF023")
            .Which.Severity.Should()
            .Be(DiagnosticSeverity.Error);
        driver.GetRunResult().GeneratedTrees.Should().BeEmpty();
    }

    private static string _Generate(string source)
    {
        // The output compilation includes the generated module, so no error here also proves the factory compiles.
        var driver = GeneratorTestHelper.Run(source, out var diagnostics);

        diagnostics.Should().NotContain(diagnostic => diagnostic.Severity >= DiagnosticSeverity.Warning);

        return string.Join('\n', driver.GetRunResult().GeneratedTrees.Select(tree => tree.GetText().ToString()));
    }
}
