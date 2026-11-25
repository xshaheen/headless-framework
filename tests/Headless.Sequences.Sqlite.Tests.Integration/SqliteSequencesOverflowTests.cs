// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Sequences;
using Headless.Testing.Tests;
using Headless.UnitOfWork;

namespace Tests;

[Collection<SqliteSequencesFixture>]
public sealed class SqliteSequencesOverflowTests(SqliteSequencesFixture fixture) : TestBase
{
    [Fact]
    public async Task should_reject_an_overflowing_reservation_and_still_issue_the_last_value()
    {
        const string name = "reserve-overflow";
        var key = new SequenceKey("", name, "");
        await using var host = await fixture.CreateHostAsync(
            setup => setup.Policy(name, new SequencePolicy { Start = long.MaxValue - 1 }),
            AbortToken
        );

        (await host.Generator.NextAsync(name, cancellationToken: AbortToken)).Should().Be(long.MaxValue - 1);

        var reserve = async () => await host.Generator.ReserveAsync(name, 2, cancellationToken: AbortToken);
        await reserve.Should().ThrowAsync<OverflowException>();

        (await fixture.ReadValueAsync(key, AbortToken)).Should().Be(long.MaxValue - 1);
        (await host.Generator.NextAsync(name, cancellationToken: AbortToken)).Should().Be(long.MaxValue);

        var next = async () => await host.Generator.NextAsync(name, cancellationToken: AbortToken);
        await next.Should().ThrowAsync<OverflowException>();

        (await fixture.ReadValueAsync(key, AbortToken)).Should().Be(long.MaxValue);
    }

    [Fact]
    public async Task should_reject_an_overflowing_block_refill_without_changing_the_counter()
    {
        const string name = "block-overflow";
        await using var host = await fixture.CreateHostAsync(
            setup => setup.Policy(name, new SequencePolicy { Start = long.MaxValue - 1, BlockSize = 2 }),
            AbortToken
        );

        (await host.Generator.NextAsync(name, cancellationToken: AbortToken)).Should().Be(long.MaxValue - 1);
        (await host.Generator.NextAsync(name, cancellationToken: AbortToken)).Should().Be(long.MaxValue);

        var next = async () => await host.Generator.NextAsync(name, cancellationToken: AbortToken);
        await next.Should().ThrowAsync<OverflowException>();

        (await fixture.ReadValueAsync(new SequenceKey("", name, ""), AbortToken)).Should().Be(long.MaxValue);
    }

    [Fact]
    public async Task should_reject_gap_free_overflow_and_allow_the_unit_to_commit_the_last_value()
    {
        const string name = "gap-free-overflow";
        await using var host = await fixture.CreateHostAsync(
            setup => setup.Policy(name, new SequencePolicy { Start = long.MaxValue, Mode = SequenceMode.GapFree }),
            AbortToken
        );
        await using var unit = await fixture.BeginUnitAsync(host, AbortToken);

        (await unit.Unit.Sequences.NextAsync(name, cancellationToken: AbortToken)).Should().Be(long.MaxValue);

        var next = async () => await unit.Unit.Sequences.NextAsync(name, cancellationToken: AbortToken);
        await next.Should().ThrowAsync<OverflowException>();
        await unit.CommitAsync(AbortToken);

        (await fixture.ReadValueAsync(new SequenceKey("", name, ""), AbortToken)).Should().Be(long.MaxValue);
    }
}
