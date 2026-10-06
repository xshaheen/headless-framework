// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Tests;

namespace Tests;

/// <summary>The HF2001 fix moves a one-to-one publish or enqueue onto the unit's outbox.</summary>
public sealed class HF2001OutboxCodeFixTests : TestBase
{
    [Fact]
    public async Task should_rewrite_the_publish_onto_the_outbox_and_add_the_namespace_import()
    {
        var fixedSource = await AnalyzerHarness.FixAsync(
            """
            using System.Threading;
            using System.Threading.Tasks;
            using Headless.Messaging;
            using Microsoft.EntityFrameworkCore;

            public sealed record OrderPlaced(int Id);

            public sealed class Handler(IBus bus, Headless.UnitOfWork.IUnitOfWorkFactory factory)
            {
                public Task Handle(DbContext db, CancellationToken ct) =>
                    Headless.UnitOfWork.UnitOfWorkFactoryEntityFrameworkExtensions.RunAsync(factory, db, async (unit, token) =>
                    {
                        await bus.PublishAsync(new OrderPlaced(1), token);
                    }, cancellationToken: ct);
            }
            """,
            AbortToken
        );

        fixedSource.Should().Contain("await unit.Outbox.PublishAsync(new OrderPlaced(1), token);");
        fixedSource.Should().Contain("using Headless.UnitOfWork;");
    }

    [Fact]
    public async Task should_not_add_an_import_the_file_already_has()
    {
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Headless.Messaging;
            using Headless.UnitOfWork;
            using Microsoft.EntityFrameworkCore;

            public sealed record OrderPlaced(int Id);

            public sealed class Handler(IQueue queue, IUnitOfWorkFactory factory)
            {
                public Task Handle(DbContext db, CancellationToken ct) =>
                    factory.RunAsync(db, async (unit, token) =>
                    {
                        // Enqueue after the order row is written.
                        await queue.EnqueueAsync(contentObj: new OrderPlaced(1), cancellationToken: token);
                    }, cancellationToken: ct);
            }
            """;

        var fixedSource = await AnalyzerHarness.FixAsync(source, AbortToken);

        fixedSource
            .Should()
            .Be(
                source.Replace("await queue.EnqueueAsync(", "await unit.Outbox.EnqueueAsync(", StringComparison.Ordinal)
            );
    }

    [Fact]
    public async Task should_offer_no_fix_for_an_overload_the_outbox_lacks()
    {
        foreach (
            var call in new[]
            {
                "await bus.PublishAsync(new OrderPlaced(1), new PublishOptions(), token);",
                "await bus.PublishAsync(new OrderPlaced(1), options => { }, token);",
                "_ = bus?.PublishAsync(new OrderPlaced(1), token);",
            }
        )
        {
            var fixes = await AnalyzerHarness.GetFixesAsync(
                $$"""
                using System.Threading;
                using System.Threading.Tasks;
                using Headless.Messaging;
                using Headless.UnitOfWork;
                using Microsoft.EntityFrameworkCore;

                public sealed record OrderPlaced(int Id);

                public sealed class Handler(IBus bus, IUnitOfWorkFactory factory)
                {
                    public Task Handle(DbContext db, CancellationToken ct) =>
                        factory.RunAsync(db, async (unit, token) => { {{call}} }, cancellationToken: ct);
                }
                """,
                AbortToken
            );

            fixes.Should().BeEmpty(call);
        }
    }

    [Fact]
    public async Task should_fix_all_calls_in_a_document_and_import_the_namespace_once()
    {
        var fixedSource = await AnalyzerHarness.FixAllAsync(
            """
            using System.Threading;
            using System.Threading.Tasks;
            using Headless.Messaging;
            using Microsoft.EntityFrameworkCore;

            public sealed record OrderPlaced(int Id);

            public sealed class Handler(IBus bus, IQueue queue, Headless.UnitOfWork.IUnitOfWorkFactory factory)
            {
                public Task Handle(DbContext db, CancellationToken ct) =>
                    Headless.UnitOfWork.UnitOfWorkFactoryEntityFrameworkExtensions.RunAsync(factory, db, async (unit, token) =>
                    {
                        await bus.PublishAsync(new OrderPlaced(1), token);
                        await queue.EnqueueAsync(await bus.PublishAsync(new OrderPlaced(2), token), token);
                    }, cancellationToken: ct);
            }
            """,
            AbortToken
        );

        fixedSource.Should().Contain("await unit.Outbox.PublishAsync(new OrderPlaced(1), token);");
        fixedSource
            .Should()
            .Contain(
                "await unit.Outbox.EnqueueAsync(await unit.Outbox.PublishAsync(new OrderPlaced(2), token), token);"
            );
        fixedSource.Split("using Headless.UnitOfWork;").Should().HaveCount(2, "the import is added exactly once");
    }
}
