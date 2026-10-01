// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.CodeAnalysis;

namespace Tests;

/// <summary>
/// Declarations the generator must turn into code that compiles, and declarations the language itself must reject
/// before the generator is involved.
/// </summary>
public sealed class GeneratedSourceCompilationTests
{
    [Fact]
    public void should_not_compile_every_instance_delivery_on_the_queue_lane()
    {
        // given
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Headless.Messaging;

            namespace Billing;

            public sealed record IssueInvoiceCommand(string OrderId);

            [QueueConsumer("billing.issue-invoice", EveryInstance = true)]
            public sealed class IssueInvoice : IConsume<IssueInvoiceCommand>
            {
                public ValueTask ConsumeAsync(ConsumeContext<IssueInvoiceCommand> context, CancellationToken cancellationToken) => default;
            }
            """;

        // when
        GeneratorTestHelper.Run(source, out var diagnostics);

        // then
        // The compiler reports a named argument that names no member of the attribute as CS0246 on that name.
        var error = diagnostics
            .Should()
            .ContainSingle(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Which;
        error.Id.Should().Be("CS0246");
        error.GetMessage(CultureInfo.InvariantCulture).Should().Contain("EveryInstance");
    }

    [Fact]
    public void should_compile_when_a_namespace_segment_matches_the_assembly_name_suffix()
    {
        // Generated code lives in `namespace Messaging.SourceGenerator.Tests`, where an unqualified `Tests.X` would bind
        // to that namespace instead of the top-level `Tests`.
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Headless.Messaging;

            namespace Tests;

            public sealed record Payload(int Id);

            [QueueConsumer("tests.payload")]
            public sealed class PayloadConsumer : IConsume<Payload>
            {
                public ValueTask ConsumeAsync(ConsumeContext<Payload> context, CancellationToken cancellationToken) => default;
            }
            """;

        GeneratorTestHelper.Run(source, out var diagnostics);

        diagnostics.Should().NotContain(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void should_emit_a_module_the_host_adds_and_no_module_initializer()
    {
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Headless.Messaging;
            using Headless.Messaging.Registration;

            namespace Demo;

            public sealed record Payload(int Id);

            [BusConsumer("demo.payload")]
            public sealed class PayloadConsumer : IConsume<Payload>
            {
                public ValueTask ConsumeAsync(ConsumeContext<Payload> context, CancellationToken cancellationToken) => default;
            }

            public static class Module
            {
                public static void Configure(MessagingContributionBuilder messaging) =>
                    messaging.AddModule<global::Messaging.SourceGenerator.Tests.MessagingModule>();
            }
            """;

        var driver = GeneratorTestHelper.Run(source, out var diagnostics);

        diagnostics.Should().NotContain(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var generated = driver.GetRunResult().GeneratedTrees.Single().ToString();
        generated
            .Should()
            .Contain("public sealed class MessagingModule : global::Headless.Messaging.IMessagingModule")
            .And.NotContain("ModuleInitializer");
    }
}
