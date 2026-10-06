// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Tests;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Tests;

/// <summary>HF2001: a bus publish or queue enqueue while a unit of work is in scope.</summary>
public sealed class HF2001OutboxTests : TestBase
{
    private const string _Prelude = """
        using System.Threading;
        using System.Threading.Tasks;
        using Headless.Messaging;
        using Headless.UnitOfWork;
        using Microsoft.EntityFrameworkCore;

        public sealed record OrderPlaced(int Id);

        """;

    [Fact]
    public async Task should_report_bus_publish_and_queue_enqueue_and_name_the_outbox()
    {
        var diagnostics = await AnalyzerHarness.AnalyzeAsync(
            _Prelude
                + """
                public sealed class Handler(IBus bus, IQueue queue, IUnitOfWorkFactory factory)
                {
                    public Task Handle(DbContext db, CancellationToken ct) =>
                        factory.RunAsync(db, async (unit, token) =>
                        {
                            await bus.PublishAsync(new OrderPlaced(1), token);
                            await queue.EnqueueAsync(new OrderPlaced(2), token);
                        }, cancellationToken: ct);
                }
                """,
            AbortToken
        );

        diagnostics.Should().HaveCount(2);
        diagnostics.Should().AllSatisfy(diagnostic => diagnostic.Id.Should().Be("HF2001"));
        diagnostics
            .Should()
            .AllSatisfy(diagnostic =>
                diagnostic
                    .GetMessage(CultureInfo.InvariantCulture)
                    .Should()
                    .Be(
                        "This call commits on its own while unit of work 'unit' is in scope; call it through 'unit.Outbox' so the message commits with the unit"
                    )
            );
    }

    [Fact]
    public async Task should_report_the_options_and_builder_overloads()
    {
        var diagnostics = await AnalyzerHarness.AnalyzeAsync(
            _Prelude
                + """
                public sealed class Handler(IBus bus, IQueue queue, IUnitOfWorkFactory factory)
                {
                    public Task Handle(DbContext db, CancellationToken ct) =>
                        factory.RunAsync(db, async (unit, token) =>
                        {
                            await bus.PublishAsync(new OrderPlaced(1), new PublishOptions(), token);
                            await bus.PublishAsync(new OrderPlaced(2), options => { }, token);
                            await queue.EnqueueAsync(new OrderPlaced(3), new QueueOptions(), token);
                        }, cancellationToken: ct);
                }
                """,
            AbortToken
        );

        diagnostics.Should().HaveCount(3);
        diagnostics.Should().AllSatisfy(diagnostic => diagnostic.Id.Should().Be("HF2001"));
    }

    [Fact]
    public async Task should_not_report_a_publish_through_the_outbox()
    {
        var diagnostics = await AnalyzerHarness.AnalyzeAsync(
            _Prelude
                + """
                public sealed class Handler(IUnitOfWorkFactory factory)
                {
                    public Task Handle(DbContext db, CancellationToken ct) =>
                        factory.RunAsync(db, async (unit, token) =>
                        {
                            await unit.Outbox.PublishAsync(new OrderPlaced(1), token);
                            await unit.Outbox.EnqueueAsync(new OrderPlaced(2), token);
                        }, cancellationToken: ct);
                }
                """,
            AbortToken
        );

        diagnostics.Should().BeEmpty();
    }

    [Fact]
    public async Task should_stay_silent_when_the_unit_of_work_abstractions_are_not_referenced()
    {
        // A project that defines its own IBus but never references Headless.UnitOfWork.Abstractions has no unit to name.
        var compilation = CSharpCompilation.Create(
            "NoUnitOfWork",
            [
                CSharpSyntaxTree.ParseText(
                    """
                    namespace Headless.Messaging
                    {
                        public interface IBus { System.Threading.Tasks.Task PublishAsync<T>(T message); }
                    }

                    public sealed class Handler(Headless.Messaging.IBus bus)
                    {
                        public System.Threading.Tasks.Task Handle() => bus.PublishAsync(1);
                    }
                    """,
                    GeneratorCompilation.ParseOptions,
                    cancellationToken: AbortToken
                ),
            ],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );

        var diagnostics = await compilation
            .WithAnalyzers([new Headless.UnitOfWork.Analyzers.AutonomousReceiverAnalyzer()])
            .GetAnalyzerDiagnosticsAsync(AbortToken);

        diagnostics.Should().BeEmpty();
    }
}
