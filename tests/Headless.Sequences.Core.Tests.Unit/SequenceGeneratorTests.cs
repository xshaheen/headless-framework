// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Sequences;
using Headless.Testing.Tests;

namespace Tests;

public sealed class SequenceGeneratorTests : TestBase
{
    [Fact]
    public async Task should_use_the_default_policy_for_an_unregistered_name()
    {
        // given
        var context = new SequenceTestContext();

        // when
        var value = await context.Generator.NextAsync("receipt", cancellationToken: AbortToken);

        // then
        value.Should().Be(1);
        await context.Store.Received(1).IncrementAsync(new SequenceKey("", "receipt", ""), 1, 1, AbortToken);
    }

    [Fact]
    public async Task should_pass_a_registered_start_and_step()
    {
        // given
        var context = new SequenceTestContext();
        context.Options.Policies["receipt"] = new SequencePolicy { Start = 1000, Step = 10 };

        // when
        await context.Generator.NextAsync("receipt", cancellationToken: AbortToken);

        // then
        await context.Store.Received(1).IncrementAsync(Arg.Any<SequenceKey>(), 1000, 10, AbortToken);
    }

    [Fact]
    public async Task should_refuse_a_gap_free_name_naming_unit_sequences_before_the_store()
    {
        // given
        var context = new SequenceTestContext().GapFree("invoice");

        // when
        var act = async () => await context.Generator.NextAsync("invoice", cancellationToken: AbortToken);

        // then
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*unit.Sequences*");
        context.Store.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task should_refuse_to_reserve_from_a_gap_free_name()
    {
        // given
        var context = new SequenceTestContext().GapFree("invoice");

        // when
        var act = async () => await context.Generator.ReserveAsync("invoice", 5, cancellationToken: AbortToken);

        // then
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*unit.Sequences*");
        context.Store.ReceivedCalls().Should().BeEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task should_refuse_a_reserve_count_below_one(int count)
    {
        // given
        var context = new SequenceTestContext();

        // when
        var act = async () => await context.Generator.ReserveAsync("batch", count, cancellationToken: AbortToken);

        // then
        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
        context.Store.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task should_refuse_a_reserve_whose_block_overflows_before_the_store()
    {
        // given
        var context = new SequenceTestContext();
        context.Options.Policies["batch"] = new SequencePolicy { Step = long.MaxValue / 2 };

        // when
        var act = async () => await context.Generator.ReserveAsync("batch", 3, cancellationToken: AbortToken);

        // then
        await act.Should().ThrowAsync<OverflowException>();
        context.Store.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task should_create_a_new_block_at_the_policy_start()
    {
        // given
        var context = new SequenceTestContext();
        context.Options.Policies["batch"] = new SequencePolicy { Start = 1000, Step = 10 };

        // when
        var range = await context.Generator.ReserveAsync("batch", 5, cancellationToken: AbortToken);

        // then — a new row stores the block's last value, and an existing row advances by the whole block
        await context.Store.Received(1).IncrementAsync(Arg.Any<SequenceKey>(), 1040, 50, AbortToken);
        (range == new SequenceRange(1000, 5, 10)).Should().BeTrue();
        range.Last.Should().Be(1040);
    }

    [Fact]
    public async Task should_map_the_returned_last_value_back_to_its_block()
    {
        // given — the counter already stood at 1040, so the store returns 1090
        var context = new SequenceTestContext();
        context.Options.Policies["batch"] = new SequencePolicy { Start = 1000, Step = 10 };
        context
            .Store.IncrementAsync(
                Arg.Any<SequenceKey>(),
                Arg.Any<long>(),
                Arg.Any<long>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(ValueTask.FromResult(1090L));

        // when
        var range = await context.Generator.ReserveAsync("batch", 5, cancellationToken: AbortToken);

        // then
        range.Should().Equal(1050L, 1060L, 1070L, 1080L, 1090L);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task should_refuse_a_blank_name(string name)
    {
        var context = new SequenceTestContext();

        var act = async () => await context.Generator.NextAsync(name, cancellationToken: AbortToken);

        await act.Should().ThrowAsync<ArgumentException>();
        context.Store.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task should_refuse_a_null_name()
    {
        var context = new SequenceTestContext();

        var act = async () => await context.Generator.NextAsync(null!, cancellationToken: AbortToken);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task should_refuse_an_over_length_name_and_accept_one_at_the_limit()
    {
        // given
        var context = new SequenceTestContext();
        var atLimit = new string('n', SequenceFieldLimits.NameMaxLength);

        // when
        var tooLong = async () => await context.Generator.NextAsync(atLimit + "n", cancellationToken: AbortToken);

        // then
        await tooLong.Should().ThrowAsync<ArgumentException>();
        await context.Generator.NextAsync(atLimit, cancellationToken: AbortToken);
        await context.Store.Received(1).IncrementAsync(new SequenceKey("", atLimit, ""), 1, 1, AbortToken);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task should_map_a_missing_partition_to_empty(string? partition)
    {
        var context = new SequenceTestContext();

        await context.Generator.NextAsync("receipt", partition, AbortToken);

        await context.Store.Received(1).IncrementAsync(new SequenceKey("", "receipt", ""), 1, 1, AbortToken);
    }

    [Fact]
    public async Task should_refuse_a_whitespace_or_over_length_partition()
    {
        // given
        var context = new SequenceTestContext();
        var tooLong = new string('p', SequenceFieldLimits.PartitionMaxLength + 1);

        // when
        var whitespace = async () => await context.Generator.NextAsync("receipt", "  ", AbortToken);
        var overLength = async () => await context.Generator.NextAsync("receipt", tooLong, AbortToken);

        // then
        await whitespace.Should().ThrowAsync<ArgumentException>();
        await overLength.Should().ThrowAsync<ArgumentException>();
        context.Store.ReceivedCalls().Should().BeEmpty();
    }

    [Theory]
    [InlineData("receipt ", null, null)]
    [InlineData(" receipt", null, null)]
    [InlineData("receipt", "2026 ", null)]
    [InlineData("receipt", "\t2026", null)]
    [InlineData("receipt", null, "t1 ")]
    [InlineData("receipt", null, " t1")]
    public async Task should_refuse_a_key_part_padded_with_whitespace(string name, string? partition, string? tenantId)
    {
        // given
        var context = new SequenceTestContext();
        context.Tenant.Id = tenantId;

        // when
        var act = async () => await context.Generator.NextAsync(name, partition, AbortToken);

        // then
        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*whitespace*");
        context.Store.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task should_key_the_counter_by_the_current_tenant_and_partition()
    {
        // given
        var context = new SequenceTestContext();
        context.Tenant.Id = "t1";

        // when
        await context.Generator.NextAsync("receipt", "2026", AbortToken);

        // then
        await context.Store.Received(1).IncrementAsync(new SequenceKey("t1", "receipt", "2026"), 1, 1, AbortToken);
    }

    [Fact]
    public async Task should_read_the_tenant_on_every_call()
    {
        // given
        var context = new SequenceTestContext();
        context.Tenant.Id = "t1";
        await context.Generator.NextAsync("receipt", cancellationToken: AbortToken);

        // when
        using (context.Tenant.Change(null))
        {
            await context.Generator.NextAsync("receipt", cancellationToken: AbortToken);
        }

        // then
        await context.Store.Received(1).IncrementAsync(new SequenceKey("t1", "receipt", ""), 1, 1, AbortToken);
        await context.Store.Received(1).IncrementAsync(new SequenceKey("", "receipt", ""), 1, 1, AbortToken);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public async Task should_refuse_an_empty_or_whitespace_tenant_id(string tenantId)
    {
        // given
        var context = new SequenceTestContext();
        context.Tenant.Id = tenantId;

        // when
        var act = async () => await context.Generator.NextAsync("receipt", cancellationToken: AbortToken);

        // then
        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*tenant id*");
        context.Store.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task should_refuse_an_over_length_tenant_id()
    {
        // given
        var context = new SequenceTestContext();
        context.Tenant.Id = new string('t', SequenceFieldLimits.TenantIdMaxLength + 1);

        // when
        var act = async () => await context.Generator.NextAsync("receipt", cancellationToken: AbortToken);

        // then
        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*tenant id*");
        context.Store.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public void should_keep_the_key_within_the_sql_server_index_limit()
    {
        // nvarchar stores two bytes per character, and a clustered key allows 900 bytes
        (
            SequenceFieldLimits.TenantIdMaxLength
            + SequenceFieldLimits.NameMaxLength
            + SequenceFieldLimits.PartitionMaxLength
        )
            .Should()
            .BeLessThanOrEqualTo(450);
    }
}
