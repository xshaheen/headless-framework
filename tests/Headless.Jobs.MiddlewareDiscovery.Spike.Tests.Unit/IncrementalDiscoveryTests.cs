// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Tests;
using Microsoft.CodeAnalysis;

namespace Tests;

public sealed class IncrementalDiscoveryTests : TestBase
{
    [Fact]
    public void should_regenerate_when_referenced_priority_changes()
    {
        var initialConsumer = CompilationFixture.CreateCompilation(
            "Consumer",
            FixtureSources.Consumer,
            CompilationFixture.ProducerReference
        );
        var initialRun = CompilationFixture.Run(initialConsumer);
        var updatedProducer = CompilationFixture.CreateCompilation(
            "Producer",
            FixtureSources.Producer.Replace(
                "typeof(Spike.Producer.GlobalMiddleware), 10",
                "typeof(Spike.Producer.GlobalMiddleware), -10",
                StringComparison.Ordinal
            )
        );
        var updatedConsumer = CompilationFixture.CreateCompilation(
            "Consumer",
            FixtureSources.Consumer,
            CompilationFixture.EmitReference(updatedProducer)
        );

        var updatedRun = CompilationFixture.Run(updatedConsumer, initialRun.Driver);

        updatedRun.GeneratedSource.Should().NotBe(initialRun.GeneratedSource);
        updatedRun
            .GeneratedSource.IndexOf("GlobalMiddleware.Invoke", StringComparison.Ordinal)
            .Should()
            .BeLessThan(updatedRun.GeneratedSource.IndexOf("LocalMiddleware.Invoke", StringComparison.Ordinal));
        updatedRun.TrackedSteps.Should().ContainKey("MiddlewareDiscovery");
        updatedRun
            .TrackedSteps["MiddlewareDiscovery"]
            .SelectMany(static step => step.Outputs)
            .Should()
            .Contain(static output => output.Reason == IncrementalStepRunReason.Modified);
    }

    [Fact]
    public void should_regenerate_when_referenced_target_identity_changes()
    {
        var initialConsumer = CompilationFixture.CreateCompilation(
            "Consumer",
            FixtureSources.Consumer,
            CompilationFixture.ProducerReference
        );
        var initialRun = CompilationFixture.Run(initialConsumer);
        var updatedProducer = CompilationFixture.CreateCompilation(
            "Producer",
            FixtureSources.Producer.Replace("producer-job", "renamed-job", StringComparison.Ordinal)
        );
        var updatedConsumer = CompilationFixture.CreateCompilation(
            "Consumer",
            FixtureSources.Consumer,
            CompilationFixture.EmitReference(updatedProducer)
        );

        var updatedRun = CompilationFixture.Run(updatedConsumer, initialRun.Driver);

        updatedRun.GeneratorDiagnostics.Should().BeEmpty();
        updatedRun.GeneratedSource.Should().Contain("renamed-job");
        updatedRun.GeneratedSource.Should().NotContain("producer-job");
        updatedRun
            .TrackedSteps["MiddlewareDiscovery"]
            .SelectMany(static step => step.Outputs)
            .Should()
            .Contain(static output => output.Reason == IncrementalStepRunReason.Modified);
    }

    [Fact]
    public void should_report_when_referenced_descriptor_identity_changes_without_middleware_target()
    {
        var initialConsumer = CompilationFixture.CreateCompilation(
            "Consumer",
            FixtureSources.Consumer,
            CompilationFixture.ProducerReference
        );
        var initialRun = CompilationFixture.Run(initialConsumer);
        var updatedProducer = CompilationFixture.CreateCompilation(
            "Producer",
            FixtureSources.Producer.Replace(
                "[assembly: JobFunctionDescriptor(\"producer-job\")]",
                "[assembly: JobFunctionDescriptor(\"renamed-job\")]",
                StringComparison.Ordinal
            )
        );
        var updatedConsumer = CompilationFixture.CreateCompilation(
            "Consumer",
            FixtureSources.Consumer,
            CompilationFixture.EmitReference(updatedProducer)
        );

        var updatedRun = CompilationFixture.Run(updatedConsumer, initialRun.Driver);

        updatedRun.GeneratorDiagnostics.Should().ContainSingle(static diagnostic => diagnostic.Id == "JMD001");
        updatedRun.GeneratedSource.Should().NotContain("global::Spike.Producer.TargetedMiddleware.Invoke");
        updatedRun
            .TrackedSteps["MiddlewareDiscovery"]
            .SelectMany(static step => step.Outputs)
            .Should()
            .Contain(static output => output.Reason == IncrementalStepRunReason.Modified);
    }

    [Fact]
    public void should_preserve_generated_output_when_unrelated_consumer_source_changes()
    {
        var producerReference = CompilationFixture.ProducerReference;
        var initialConsumer = CompilationFixture.CreateCompilation(
            "Consumer",
            FixtureSources.Consumer,
            producerReference
        );
        var initialRun = CompilationFixture.Run(initialConsumer);
        var updatedConsumer = CompilationFixture.CreateCompilation(
            "Consumer",
            FixtureSources.Consumer + Environment.NewLine + "namespace Unrelated { public sealed class Added; }",
            producerReference
        );

        var updatedRun = CompilationFixture.Run(updatedConsumer, initialRun.Driver);

        updatedRun.GeneratedSource.Should().Be(initialRun.GeneratedSource);
    }
}
