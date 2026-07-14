// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using Headless.Testing.Tests;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Tests;

public sealed class MiddlewareDiscoverySpikeGeneratorTests : TestBase
{
    [Fact]
    public void should_discover_global_and_targeted_middleware_across_assembly_boundary()
    {
        var consumer = CompilationFixture.CreateCompilation(
            "Consumer",
            FixtureSources.Consumer,
            CompilationFixture.ProducerReference
        );

        var run = CompilationFixture.Run(consumer);

        run.GeneratorDiagnostics.Should().BeEmpty();
        run.GeneratedSource.Should().Contain("global::Spike.Consumer.LocalMiddleware.Invoke(services);");
        run.GeneratedSource.Should().Contain("global::Spike.Consumer.LocalTargetedMiddleware.Invoke(services);");
        run.GeneratedSource.Should().Contain("global::Spike.Producer.GlobalMiddleware.Invoke(services);");
        run.GeneratedSource.Should().Contain("global::Spike.Producer.TargetedMiddleware.Invoke(services);");
        run.GeneratedSource.Should().Contain("producer-job");
        var consumerChain = run.GeneratedSource[
            run.GeneratedSource.IndexOf(
                "case \"consumer-job\":",
                StringComparison.Ordinal
            )..run.GeneratedSource.IndexOf("case \"producer-job\":", StringComparison.Ordinal)
        ];
        consumerChain
            .IndexOf("LocalMiddleware.Invoke", StringComparison.Ordinal)
            .Should()
            .BeLessThan(consumerChain.IndexOf("LocalTargetedMiddleware.Invoke", StringComparison.Ordinal));
        consumerChain
            .IndexOf("LocalTargetedMiddleware.Invoke", StringComparison.Ordinal)
            .Should()
            .BeLessThan(consumerChain.IndexOf("AlphaLocalMiddleware.Invoke", StringComparison.Ordinal));
        consumerChain
            .IndexOf("AlphaLocalMiddleware.Invoke", StringComparison.Ordinal)
            .Should()
            .BeLessThan(consumerChain.IndexOf("BetaMiddleware.Invoke", StringComparison.Ordinal));
        consumerChain
            .IndexOf("BetaMiddleware.Invoke", StringComparison.Ordinal)
            .Should()
            .BeLessThan(consumerChain.IndexOf("GlobalMiddleware.Invoke", StringComparison.Ordinal));
        run.OutputCompilation.GetDiagnostics(AbortToken)
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Should()
            .BeEmpty();
    }

    [Fact]
    public void should_order_by_priority_then_stable_identity_independent_of_reference_order()
    {
        var producerReference = CompilationFixture.ProducerReference;
        var secondProducer = CompilationFixture.CreateCompilation(
            "SecondProducer",
            FixtureSources.SecondProducer,
            producerReference
        );
        var secondProducerReference = CompilationFixture.EmitReference(secondProducer);
        var forwardConsumer = CompilationFixture.CreateCompilation(
            "Consumer",
            FixtureSources.Consumer,
            producerReference,
            secondProducerReference
        );
        var reverseConsumer = CompilationFixture.CreateCompilation(
            "Consumer",
            FixtureSources.Consumer,
            secondProducerReference,
            producerReference
        );
        var reversedDeclarationsConsumer = CompilationFixture.CreateCompilation(
            "Consumer",
            FixtureSources.Consumer.Replace(
                "[assembly: JobMiddleware(typeof(Spike.Consumer.AlphaLocalMiddleware), 10)]\n[assembly: JobMiddleware(typeof(Spike.Consumer.BetaMiddleware), 10)]",
                "[assembly: JobMiddleware(typeof(Spike.Consumer.BetaMiddleware), 10)]\n[assembly: JobMiddleware(typeof(Spike.Consumer.AlphaLocalMiddleware), 10)]",
                StringComparison.Ordinal
            ),
            producerReference,
            secondProducerReference
        );

        var forward = CompilationFixture.Run(forwardConsumer);
        var reverse = CompilationFixture.Run(reverseConsumer);
        var reversedDeclarations = CompilationFixture.Run(reversedDeclarationsConsumer);

        forward.GeneratedSource.Should().Be(reverse.GeneratedSource);
        forward.GeneratedSource.Should().Be(reversedDeclarations.GeneratedSource);
        forward
            .GeneratedSource.IndexOf("LocalMiddleware.Invoke", StringComparison.Ordinal)
            .Should()
            .BeLessThan(forward.GeneratedSource.IndexOf("AlphaLocalMiddleware.Invoke", StringComparison.Ordinal));
        forward
            .GeneratedSource.IndexOf("AlphaLocalMiddleware.Invoke", StringComparison.Ordinal)
            .Should()
            .BeLessThan(forward.GeneratedSource.IndexOf("BetaMiddleware.Invoke", StringComparison.Ordinal));
        forward
            .GeneratedSource.IndexOf("BetaMiddleware.Invoke", StringComparison.Ordinal)
            .Should()
            .BeLessThan(forward.GeneratedSource.IndexOf("GlobalMiddleware.Invoke", StringComparison.Ordinal));
        forward
            .GeneratedSource.IndexOf("GlobalMiddleware.Invoke", StringComparison.Ordinal)
            .Should()
            .BeLessThan(forward.GeneratedSource.IndexOf("AlphaMiddleware.Invoke", StringComparison.Ordinal));
    }

    [Fact]
    public void should_report_missing_target_without_emitting_invalid_targeted_call()
    {
        var consumer = CompilationFixture.CreateCompilation(
            "Consumer",
            FixtureSources.ConsumerWithMissingTarget,
            CompilationFixture.ProducerReference
        );

        var run = CompilationFixture.Run(consumer);

        run.GeneratorDiagnostics.Should()
            .ContainSingle()
            .Which.Should()
            .Match<Diagnostic>(static diagnostic =>
                diagnostic.Id == "JMD001"
                && diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.GetMessage(CultureInfo.InvariantCulture)
                    == "Middleware 'Consumer:Spike.Consumer.MissingTargetMiddleware' targets unknown job function descriptor 'missing-job'"
            );
        run.GeneratedSource.Should().NotContain("MissingTargetMiddleware.Invoke");
    }

    [Fact]
    public void should_report_exact_duplicate_without_emitting_double_invocation()
    {
        var consumer = CompilationFixture.CreateCompilation(
            "Consumer",
            FixtureSources.ConsumerWithDuplicate,
            CompilationFixture.ProducerReference
        );

        var run = CompilationFixture.Run(consumer);

        run.GeneratorDiagnostics.Should()
            .ContainSingle()
            .Which.Should()
            .Match<Diagnostic>(static diagnostic =>
                diagnostic.Id == "JMD002"
                && diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.GetMessage(CultureInfo.InvariantCulture)
                    == "Middleware declaration 'global|0|Consumer:Spike.Consumer.LocalMiddleware' is duplicated"
            );
        run.GeneratedSource.Split("LocalMiddleware.Invoke", StringSplitOptions.None).Should().HaveCount(3);
    }

    [Fact]
    public void should_derive_explicit_hook_from_consumer_marker_without_injected_hook_name()
    {
        var hookProducer = CompilationFixture.CreateCompilation("HookProducer", FixtureSources.ProducerWithoutMetadata);
        var consumer = CompilationFixture.CreateCompilation(
            "Consumer",
            FixtureSources.ConsumerWithHookMarker,
            CompilationFixture.EmitReference(hookProducer)
        );

        var run = CompilationFixture.Run(consumer);

        run.GeneratorDiagnostics.Should().BeEmpty();
        run.GeneratedSource.Should()
            .Contain("global::Spike.Producer.GeneratedJobsMiddlewareRegistration.Register(services);");
        run.GeneratedSource.Should().NotContain(".Invoke(services);");
        run.OutputCompilation.GetDiagnostics(AbortToken)
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Should()
            .BeEmpty();
    }

    [Fact]
    public void should_report_missing_derived_hook()
    {
        var hookProducer = CompilationFixture.CreateCompilation("HookProducer", FixtureSources.ProducerWithoutHook);
        var consumer = CompilationFixture.CreateCompilation(
            "Consumer",
            FixtureSources.ConsumerWithHookMarker,
            CompilationFixture.EmitReference(hookProducer)
        );

        var run = CompilationFixture.Run(consumer);

        run.GeneratorDiagnostics.Should()
            .ContainSingle()
            .Which.Should()
            .Match<Diagnostic>(static diagnostic =>
                diagnostic.Id == "JMD003"
                && diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.GetMessage(CultureInfo.InvariantCulture)
                    == "Assembly marker 'Spike.Producer.AssemblyMarker' does not expose the well-known generated middleware hook 'Spike.Producer.GeneratedJobsMiddlewareRegistration'"
            );
        run.GeneratedSource.Should().NotContain(".Register(services);");
    }

    [Fact]
    public void should_emit_direct_calls_without_runtime_discovery_or_expression_compilation()
    {
        var consumer = CompilationFixture.CreateCompilation(
            "Consumer",
            FixtureSources.Consumer,
            CompilationFixture.ProducerReference
        );

        var generatedSource = CompilationFixture.Run(consumer).GeneratedSource;

#pragma warning disable xUnit1051 // Repository tests use TestBase.AbortToken instead of TestContext directly.
        var syntaxTree = CSharpSyntaxTree.ParseText(generatedSource, cancellationToken: AbortToken);
        var root = syntaxTree.GetRoot(AbortToken);
#pragma warning restore xUnit1051
        var invocations = root.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Select(static invocation => invocation.Expression.ToString())
            .ToArray();

        invocations
            .Should()
            .BeEquivalentTo(
                [
                    "global::Spike.Consumer.LocalMiddleware.Invoke",
                    "global::Spike.Consumer.AlphaLocalMiddleware.Invoke",
                    "global::Spike.Consumer.BetaMiddleware.Invoke",
                    "global::Spike.Producer.GlobalMiddleware.Invoke",
                    "global::Spike.Consumer.LocalMiddleware.Invoke",
                    "global::Spike.Consumer.LocalTargetedMiddleware.Invoke",
                    "global::Spike.Consumer.AlphaLocalMiddleware.Invoke",
                    "global::Spike.Consumer.BetaMiddleware.Invoke",
                    "global::Spike.Producer.GlobalMiddleware.Invoke",
                    "global::Spike.Consumer.LocalMiddleware.Invoke",
                    "global::Spike.Consumer.AlphaLocalMiddleware.Invoke",
                    "global::Spike.Consumer.BetaMiddleware.Invoke",
                    "global::Spike.Producer.GlobalMiddleware.Invoke",
                    "global::Spike.Producer.TargetedMiddleware.Invoke",
                ],
                static options => options.WithStrictOrdering()
            );
        generatedSource.Should().NotContain("System.Reflection");
        generatedSource.Should().NotContain("Assembly.Load");
        generatedSource.Should().NotContain("Expression.Compile");
        generatedSource.Should().NotContain("Expression.Lambda");
    }
}

internal static class FixtureSources
{
    public const string Producer = """
        using System;
        using Spike.Contracts;

        [assembly: JobFunctionDescriptor("producer-job")]
        [assembly: JobMiddleware(typeof(Spike.Producer.GlobalMiddleware), 10)]
        [assembly: JobMiddleware(typeof(Spike.Producer.TargetedMiddleware), 20, "producer-job")]

        namespace Spike.Contracts
        {
            [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
            public sealed class JobMiddlewareAttribute(Type middlewareType, int priority, string? targetFunction = null)
                : Attribute
            {
                public Type MiddlewareType { get; } = middlewareType;
                public int Priority { get; } = priority;
                public string? TargetFunction { get; } = targetFunction;
            }

            [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
            public sealed class JobFunctionDescriptorAttribute(string identity) : Attribute
            {
                public string Identity { get; } = identity;
            }

            [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
            public sealed class IncludeJobsMiddlewareAttribute(Type assemblyMarkerType) : Attribute
            {
                public Type AssemblyMarkerType { get; } = assemblyMarkerType;
            }

            [AttributeUsage(AttributeTargets.Method)]
            public sealed class JobFunctionAttribute(string identity) : Attribute
            {
                public string Identity { get; } = identity;
            }
        }

        namespace Spike.Producer
        {
            public sealed class AssemblyMarker;

            public static class GlobalMiddleware
            {
                public static void Invoke(IServiceProvider services) { }
            }

            public static class TargetedMiddleware
            {
                public static void Invoke(IServiceProvider services) { }
            }

            public static class GeneratedJobsMiddlewareRegistration
            {
                public static void Register(IServiceProvider services) { }
            }
        }
        """;

    public static string ProducerWithoutMetadata =>
        Producer
            .Replace("[assembly: JobFunctionDescriptor(\"producer-job\")]\n", string.Empty, StringComparison.Ordinal)
            .Replace(
                "[assembly: JobMiddleware(typeof(Spike.Producer.GlobalMiddleware), 10)]\n",
                string.Empty,
                StringComparison.Ordinal
            )
            .Replace(
                "[assembly: JobMiddleware(typeof(Spike.Producer.TargetedMiddleware), 20, \"producer-job\")]\n",
                string.Empty,
                StringComparison.Ordinal
            );

    public static string ProducerWithoutHook =>
        ProducerWithoutMetadata.Replace(
            "GeneratedJobsMiddlewareRegistration",
            "UnexpectedGeneratedJobsMiddlewareRegistration",
            StringComparison.Ordinal
        );

    public const string Consumer = """
        using System;
        using Spike.Contracts;

        [assembly: JobMiddleware(typeof(Spike.Consumer.LocalMiddleware), 0)]
        [assembly: JobMiddleware(typeof(Spike.Consumer.AlphaLocalMiddleware), 10)]
        [assembly: JobMiddleware(typeof(Spike.Consumer.BetaMiddleware), 10)]
        [assembly: JobMiddleware(typeof(Spike.Consumer.LocalTargetedMiddleware), 5, "consumer-job")]

        namespace Spike.Consumer
        {
            public static class LocalMiddleware
            {
                public static void Invoke(IServiceProvider services) { }
            }

            public static class BetaMiddleware
            {
                public static void Invoke(IServiceProvider services) { }
            }

            public static class AlphaLocalMiddleware
            {
                public static void Invoke(IServiceProvider services) { }
            }

            public static class LocalTargetedMiddleware
            {
                public static void Invoke(IServiceProvider services) { }
            }

            public sealed class Jobs
            {
                [JobFunction("consumer-job")]
                public void Run() { }
            }
        }
        """;

    public const string SecondProducer = """
        using System;
        using Spike.Contracts;

        [assembly: JobMiddleware(typeof(Spike.SecondProducer.AlphaMiddleware), 10)]

        namespace Spike.SecondProducer
        {
            public static class AlphaMiddleware
            {
                public static void Invoke(IServiceProvider services) { }
            }
        }
        """;

    public const string ConsumerWithMissingTarget = """
        using System;
        using Spike.Contracts;

        [assembly: JobMiddleware(typeof(Spike.Consumer.MissingTargetMiddleware), 0, "missing-job")]

        namespace Spike.Consumer
        {
            public static class MissingTargetMiddleware
            {
                public static void Invoke(IServiceProvider services) { }
            }
        }
        """;

    public const string ConsumerWithDuplicate = """
        using System;
        using Spike.Contracts;

        [assembly: JobMiddleware(typeof(Spike.Consumer.LocalMiddleware), 0)]
        [assembly: JobMiddleware(typeof(Spike.Consumer.LocalMiddleware), 0)]

        namespace Spike.Consumer
        {
            public static class LocalMiddleware
            {
                public static void Invoke(IServiceProvider services) { }
            }
        }
        """;

    public const string ConsumerWithHookMarker = """
        using Spike.Contracts;

        [assembly: IncludeJobsMiddleware(typeof(Spike.Producer.AssemblyMarker))]
        """;
}
