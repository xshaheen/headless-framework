// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Immutable;
using Headless.Testing.Tests;
using Microsoft.CodeAnalysis;

namespace Tests;

/// <summary>
/// Decides when a unit of work is in scope at a call. HF2001 is the probe rule: every case publishes through
/// <c>IBus</c> and asserts whether, and against which unit, the call is reported.
/// </summary>
public sealed class UnitScopeTests : TestBase
{
    private const string _Usings = """
        using System;
        using System.Data.Common;
        using System.Threading;
        using System.Threading.Tasks;
        using Headless.Messaging;
        using Headless.UnitOfWork;
        using Microsoft.EntityFrameworkCore;

        public sealed record OrderPlaced(int Id);

        """;

    [Fact]
    public async Task should_report_a_publish_inside_a_run_async_block()
    {
        var diagnostics = await _AnalyzeAsync(
            """
            public sealed class Handler(IBus bus, IUnitOfWorkFactory factory)
            {
                public Task Handle(DbContext db, CancellationToken ct) =>
                    factory.RunAsync(db, async (unit, token) => await bus.PublishAsync(new OrderPlaced(1), token), cancellationToken: ct);
            }
            """
        );

        _ShouldReport(diagnostics, "unit");
    }

    [Fact]
    public async Task should_not_report_when_db_unit_of_work_may_be_null()
    {
        var diagnostics = await _AnalyzeAsync(
            """
            public sealed class Handler(IBus bus)
            {
                public async Task Handle(DbContext db)
                {
                    var unit = db.UnitOfWork();
                    await bus.PublishAsync(new OrderPlaced(1));
                    GC.KeepAlive(unit);
                }
            }
            """
        );

        diagnostics.Should().BeEmpty();
    }

    [Fact]
    public async Task should_report_when_db_unit_of_work_is_checked_for_null()
    {
        var diagnostics = await _AnalyzeAsync(
            """
            public sealed class Handler(IBus bus)
            {
                public async Task Guarded(DbContext db)
                {
                    var unit = db.UnitOfWork();
                    if (unit is null) return;
                    await bus.PublishAsync(new OrderPlaced(1));
                }

                public async Task Thrown(DbContext db)
                {
                    var unit = db.UnitOfWork() ?? throw new InvalidOperationException();
                    await bus.PublishAsync(new OrderPlaced(2));
                }
            }
            """
        );

        diagnostics.Should().HaveCount(2);
        diagnostics.Should().AllSatisfy(diagnostic => diagnostic.GetMessage().Should().Contain("'unit.Outbox'"));
    }

    [Fact]
    public async Task should_report_when_connection_unit_of_work_is_checked_for_null()
    {
        var diagnostics = await _AnalyzeAsync(
            """
            public sealed class Handler(IBus bus)
            {
                public async Task Handle(DbConnection connection)
                {
                    if (connection.UnitOfWork() is not { } unit) return;
                    await bus.PublishAsync(new OrderPlaced(1));
                }
            }
            """
        );

        _ShouldReport(diagnostics, "unit");
    }

    [Fact]
    public async Task should_report_a_consumer_publish_only_where_the_unit_is_known_not_null()
    {
        var diagnostics = await _AnalyzeAsync(
            """
            public sealed class Consumer(IBus bus) : IConsume<OrderPlaced>
            {
                public async ValueTask ConsumeAsync(ConsumeContext<OrderPlaced> context, CancellationToken cancellationToken)
                {
                    var unit = context.UnitOfWork;
                    if (unit is not null)
                    {
                        await bus.PublishAsync(new OrderPlaced(1), cancellationToken);
                    }

                    await bus.PublishAsync(new OrderPlaced(2), cancellationToken);
                }
            }
            """
        );

        _ShouldReport(diagnostics, "unit");
        _ReportedCall(diagnostics[0]).Should().Contain("OrderPlaced(1)");
    }

    [Fact]
    public async Task should_not_report_inside_a_branch_where_the_unit_is_null()
    {
        var diagnostics = await _AnalyzeAsync(
            """
            public sealed class Handler(IBus bus)
            {
                public async Task Handle(DbContext db)
                {
                    var unit = db.UnitOfWork();
                    if (unit is null)
                    {
                        await bus.PublishAsync(new OrderPlaced(1));
                    }
                }
            }
            """
        );

        diagnostics.Should().BeEmpty();
    }

    [Fact]
    public async Task should_report_an_unchecked_nullable_unit_when_nullable_analysis_is_off()
    {
        var diagnostics = await _AnalyzeAsync(
            """
            public sealed class Handler
            {
                private readonly IBus _bus;

                public Handler(IBus bus) => _bus = bus;

                public async Task Handle(DbContext db)
                {
                    var unit = db.UnitOfWork();
                    await _bus.PublishAsync(new OrderPlaced(1));
                }
            }
            """,
            NullableContextOptions.Disable
        );

        _ShouldReport(diagnostics, "unit");
    }

    [Fact]
    public async Task should_report_a_unit_captured_by_nested_lambdas()
    {
        var diagnostics = await _AnalyzeAsync(
            """
            public sealed class Handler(IBus bus, IUnitOfWorkFactory factory)
            {
                public Task Handle(DbContext db, CancellationToken ct) =>
                    factory.RunAsync(db, async (unit, token) =>
                    {
                        Func<Task> outer = async () =>
                        {
                            Func<Task> inner = () => bus.PublishAsync(new OrderPlaced(1), token);
                            await inner();
                        };

                        await outer();
                    }, cancellationToken: ct);
            }
            """
        );

        _ShouldReport(diagnostics, "unit");
    }

    [Fact]
    public async Task should_report_a_local_function_declared_after_the_unit_and_not_one_declared_before()
    {
        var diagnostics = await _AnalyzeAsync(
            """
            public sealed class Handler(IBus bus)
            {
                public async Task Handle(DbContext db)
                {
                    async Task BeforeAsync() => await bus.PublishAsync(new OrderPlaced(1));

                    var unit = db.UnitOfWork() ?? throw new InvalidOperationException();

                    async Task AfterAsync() => await bus.PublishAsync(new OrderPlaced(2));

                    await BeforeAsync();
                    await AfterAsync();
                }
            }
            """
        );

        _ShouldReport(diagnostics, "unit");
        _ReportedCall(diagnostics[0]).Should().Contain("OrderPlaced(2)");
    }

    [Fact]
    public async Task should_not_report_a_unit_declared_after_the_call()
    {
        var diagnostics = await _AnalyzeAsync(
            """
            public sealed class Handler(IBus bus, IUnitOfWorkFactory factory)
            {
                public async Task Handle(CancellationToken ct)
                {
                    await bus.PublishAsync(new OrderPlaced(1), ct);
                    await using var unit = await factory.BeginAsync(ct);
                }
            }
            """
        );

        diagnostics.Should().BeEmpty();
    }

    [Fact]
    public async Task should_not_report_a_unit_that_is_not_yet_assigned()
    {
        var diagnostics = await _AnalyzeAsync(
            """
            public sealed class Handler(IBus bus, IUnitOfWorkFactory factory)
            {
                public async Task Handle(CancellationToken ct)
                {
                    IUnitOfWork unit;
                    await bus.PublishAsync(new OrderPlaced(1), ct);
                    unit = await factory.BeginAsync(ct);
                    await unit.DisposeAsync();
                }
            }
            """
        );

        diagnostics.Should().BeEmpty();
    }

    [Fact]
    public async Task should_not_report_inside_completion_callbacks()
    {
        var diagnostics = await _AnalyzeAsync(
            """
            public sealed class Handler(IBus bus, IUnitOfWorkFactory factory)
            {
                public Task Handle(DbContext db, CancellationToken ct) =>
                    factory.RunAsync(db, (unit, token) =>
                    {
                        unit.OnCompleted(async () => await bus.PublishAsync(new OrderPlaced(1)));
                        unit.OnFailed(async failure => await bus.PublishAsync(new OrderPlaced(2)));
                        return Task.CompletedTask;
                    }, cancellationToken: ct);
            }
            """
        );

        diagnostics.Should().BeEmpty();
    }

    [Fact]
    public async Task should_report_a_unit_declared_inside_a_completion_callback()
    {
        var diagnostics = await _AnalyzeAsync(
            """
            public sealed class Handler(IBus bus, IUnitOfWorkFactory factory)
            {
                public Task Handle(DbContext db, DbConnection connection, CancellationToken ct) =>
                    factory.RunAsync(db, (unit, token) =>
                    {
                        unit.OnCompleted(async () =>
                        {
                            var next = connection.UnitOfWork();
                            if (next is null) return;
                            await bus.PublishAsync(new OrderPlaced(1));
                        });

                        return Task.CompletedTask;
                    }, cancellationToken: ct);
            }
            """
        );

        _ShouldReport(diagnostics, "next");
    }

    [Fact]
    public async Task should_not_report_inside_a_local_function_registered_as_a_completion_callback()
    {
        var diagnostics = await _AnalyzeAsync(
            """
            public sealed class Handler(IBus bus, IUnitOfWorkFactory factory)
            {
                public Task Handle(DbContext db, CancellationToken ct) =>
                    factory.RunAsync(db, (unit, token) =>
                    {
                        unit.OnCompleted(NotifyAsync);
                        return Task.CompletedTask;

                        async ValueTask NotifyAsync() => await bus.PublishAsync(new OrderPlaced(1));
                    }, cancellationToken: ct);
            }
            """
        );

        diagnostics.Should().BeEmpty();
    }

    [Fact]
    public async Task should_not_name_an_outer_unit_inside_a_static_lambda()
    {
        var diagnostics = await _AnalyzeAsync(
            """
            public sealed class Handler(IBus bus, IUnitOfWorkFactory factory)
            {
                public Task Handle(DbContext db, CancellationToken ct) =>
                    factory.RunAsync(db, async (unit, token) =>
                    {
                        Func<IBus, Task> publish = static b => b.PublishAsync(new OrderPlaced(1));
                        await publish(bus);
                    }, cancellationToken: ct);
            }
            """
        );

        diagnostics.Should().BeEmpty();
    }

    [Fact]
    public async Task should_not_report_after_the_unit_completes_or_rolls_back()
    {
        var diagnostics = await _AnalyzeAsync(
            """
            public sealed class Handler(IBus bus, IUnitOfWorkFactory factory)
            {
                public async Task Completed(CancellationToken ct)
                {
                    await using var unit = await factory.BeginAsync(ct);
                    await unit.CompleteAsync(ct);
                    await bus.PublishAsync(new OrderPlaced(1), ct);
                }

                public async Task RolledBack(CancellationToken ct)
                {
                    await using var unit = await factory.BeginAsync(ct);
                    await unit.RollbackAsync().ConfigureAwait(false);
                    await bus.PublishAsync(new OrderPlaced(2), ct);
                }
            }
            """
        );

        diagnostics.Should().BeEmpty();
    }

    [Fact]
    public async Task should_report_after_an_early_exit_rollback()
    {
        var diagnostics = await _AnalyzeAsync(
            """
            public sealed class Handler(IBus bus, IUnitOfWorkFactory factory)
            {
                public async Task Handle(bool valid, CancellationToken ct)
                {
                    await using var unit = await factory.BeginAsync(ct);

                    if (!valid)
                    {
                        await unit.RollbackAsync();
                        return;
                    }

                    await bus.PublishAsync(new OrderPlaced(1), ct);
                    await unit.CompleteAsync(ct);
                }
            }
            """
        );

        _ShouldReport(diagnostics, "unit");
    }

    [Fact]
    public async Task should_name_the_innermost_eligible_unit()
    {
        var diagnostics = await _AnalyzeAsync(
            """
            public sealed class Handler(IBus bus, IUnitOfWorkFactory factory)
            {
                public Task Inner(DbContext db, DbConnection connection, CancellationToken ct) =>
                    factory.RunAsync(db, async (unit, token) =>
                    {
                        var nested = connection.UnitOfWork() ?? throw new InvalidOperationException();
                        await bus.PublishAsync(new OrderPlaced(1), token);
                    }, cancellationToken: ct);

                public Task Outer(DbContext db, DbConnection connection, CancellationToken ct) =>
                    factory.RunAsync(db, async (unit, token) =>
                    {
                        var nested = connection.UnitOfWork();
                        await bus.PublishAsync(new OrderPlaced(2), token);
                        GC.KeepAlive(nested);
                    }, cancellationToken: ct);
            }
            """
        );

        diagnostics.Should().HaveCount(2);
        diagnostics[0].GetMessage().Should().Contain("'nested.Outbox'");
        diagnostics[1].GetMessage().Should().Contain("'unit.Outbox'");
    }

    [Fact]
    public async Task should_not_treat_a_field_or_primary_constructor_parameter_as_a_unit_in_scope()
    {
        var diagnostics = await _AnalyzeAsync(
            """
            public sealed class Handler(IBus bus, IUnitOfWork unit)
            {
                private readonly IUnitOfWork _held = unit;

                public async Task Handle()
                {
                    await bus.PublishAsync(new OrderPlaced(1));
                    GC.KeepAlive(_held);
                }
            }
            """
        );

        diagnostics.Should().BeEmpty();
    }

    [Fact]
    public async Task should_not_report_without_a_unit_in_scope()
    {
        var diagnostics = await _AnalyzeAsync(
            """
            public sealed class Handler(IBus bus)
            {
                public Task Handle(CancellationToken ct) => bus.PublishAsync(new OrderPlaced(1), ct);
            }
            """
        );

        diagnostics.Should().BeEmpty();
    }

    private static Task<ImmutableArray<Diagnostic>> _AnalyzeAsync(
        string body,
        NullableContextOptions nullable = NullableContextOptions.Enable
    ) => AnalyzerHarness.AnalyzeAsync(_Usings + body, AbortToken, nullable);

    private static void _ShouldReport(ImmutableArray<Diagnostic> diagnostics, string unit)
    {
        diagnostics.Should().ContainSingle();
        diagnostics[0].Id.Should().Be("HF2001");
        diagnostics[0].GetMessage(CultureInfo.InvariantCulture).Should().Contain($"'{unit}.Outbox'");
    }

    private static string _ReportedCall(Diagnostic diagnostic) =>
        diagnostic
            .Location.SourceTree!.ToString()
            .Substring(diagnostic.Location.SourceSpan.Start, diagnostic.Location.SourceSpan.Length);
}
