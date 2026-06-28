// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Configuration;
using Headless.Messaging.Coordination;
using Headless.Messaging.Persistence;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Tests.Coordination;

public sealed class MessagingDeadOwnerReclaimerTests : TestBase
{
    private readonly IDataStorage _storage = Substitute.For<IDataStorage>();

    private MessagingDeadOwnerReclaimer _CreateSut(
        TimeSpan? reconcileInterval = null,
        ILogger<MessagingDeadOwnerReclaimer>? logger = null
    )
    {
        var options = new MessagingOptions();

        if (reconcileInterval is { } interval)
        {
            options.DeadNodeReconcileInterval = interval;
        }

        return new MessagingDeadOwnerReclaimer(
            _storage,
            Options.Create(options),
            logger ?? NullLogger<MessagingDeadOwnerReclaimer>.Instance
        );
    }

    private static ILogger<MessagingDeadOwnerReclaimer> _CreateCapturingLogger(List<(LogLevel Level, int Id)> captured)
    {
        var logger = Substitute.For<ILogger<MessagingDeadOwnerReclaimer>>();
        logger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);
        logger
            .When(l =>
                l.Log(
                    Arg.Any<LogLevel>(),
                    Arg.Any<EventId>(),
                    Arg.Any<object>(),
                    Arg.Any<Exception?>(),
                    Arg.Any<Func<object, Exception?, string>>()
                )
            )
            .Do(ci => captured.Add((ci.Arg<LogLevel>(), ci.Arg<EventId>().Id)));

        return logger;
    }

    [Fact]
    public async Task should_reclaim_both_tables_for_the_single_dead_owner()
    {
        // given
        var sut = _CreateSut();

        // when
        await sut.ReclaimAsync(["node@5"], AbortToken);

        // then — the published and received tables are each reclaimed exactly once with the single dead owner
        await _storage
            .Received(1)
            .ReclaimDeadPublishedOwnersAsync(
                Arg.Is<IReadOnlyCollection<string>>(owners => owners.Count == 1 && owners.Contains("node@5")),
                Arg.Any<CancellationToken>()
            );
        await _storage
            .Received(1)
            .ReclaimDeadReceivedOwnersAsync(
                Arg.Is<IReadOnlyCollection<string>>(owners => owners.Count == 1 && owners.Contains("node@5")),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_reclaim_with_none_token_not_the_incoming_token()
    {
        // given — a non-default incoming token that must NOT be re-threaded into the storage writes (KTD6)
        var sut = _CreateSut();
        using var cts = new CancellationTokenSource();

        // when
        await sut.ReclaimAsync(["node@5"], cts.Token);

        // then — storage saw CancellationToken.None, so a host-shutdown cancel cannot tear a reclaim mid-write
        await _storage
            .Received(1)
            .ReclaimDeadPublishedOwnersAsync(Arg.Any<IReadOnlyCollection<string>>(), CancellationToken.None);
        await _storage
            .Received(1)
            .ReclaimDeadReceivedOwnersAsync(Arg.Any<IReadOnlyCollection<string>>(), CancellationToken.None);
    }

    [Fact]
    public async Task should_log_reclaim_count_only_for_tables_that_recovered_rows()
    {
        // given — published recovers 2 rows, received recovers none
        var captured = new List<(LogLevel Level, int Id)>();
        var sut = _CreateSut(logger: _CreateCapturingLogger(captured));
        _storage
            .ReclaimDeadPublishedOwnersAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<int>(2));
        _storage
            .ReclaimDeadReceivedOwnersAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<int>(0));

        // when
        await sut.ReclaimAsync(["node@5"], AbortToken);

        // then — EventId 91 (MessagingDeadOwnerRowsReclaimed) fires once, for the published table only
        captured.Should().ContainSingle(e => e.Id == 91 && e.Level == LogLevel.Information);
    }

    [Fact]
    public async Task should_not_log_reclaim_count_when_no_rows_recover()
    {
        // given — both tables report zero reclaimed rows (the default substitute behavior)
        var captured = new List<(LogLevel Level, int Id)>();
        var sut = _CreateSut(logger: _CreateCapturingLogger(captured));

        // when
        await sut.ReclaimAsync(["node@5"], AbortToken);

        // then — no informational reclaim-count log is emitted
        captured.Should().NotContain(e => e.Id == 91);
    }

    [Fact]
    public void should_expose_configured_reconcile_interval()
    {
        // given
        var interval = TimeSpan.FromSeconds(42);

        // when
        var sut = _CreateSut(interval);

        // then
        sut.ReconcileInterval.Should().Be(interval);
    }
}
