// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.MultiTenancy;
using Headless.Sequences;
using Headless.Testing.Tests;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

public sealed class SequenceInspectorTests : TestBase
{
    private static readonly DateTimeOffset _Now = new(2026, 10, 8, 9, 0, 0, TimeSpan.Zero);

    private readonly SequencesOptions _options = new();
    private readonly ISequenceCounterReader _reader = Substitute.For<ISequenceCounterReader>();
    private readonly SequenceInspector _inspector;

    private SequenceCounterReadRequest? _listed;
    private SequenceCounterCountRequest? _counted;

    public SequenceInspectorTests()
    {
        var monitor = Substitute.For<IOptionsMonitor<SequencesOptions>>();
        monitor.CurrentValue.Returns(_ => _options);
        _inspector = new SequenceInspector(_reader, monitor, new FakeTimeProvider(_Now));

        _Rows();
        _reader
            .CountAsync(Arg.Do<SequenceCounterCountRequest>(r => _counted = r), Arg.Any<CancellationToken>())
            .Returns(new SequenceCounterCounts(3, 1, 1, 1, Capped: false));
    }

    [Fact]
    public async Task should_select_only_the_gap_free_registered_names_when_the_mode_is_gap_free_and_the_default_is_fast()
    {
        _options.Policies["invoice"] = new SequencePolicy { Mode = SequenceMode.GapFree };
        _options.Policies["receipt"] = new SequencePolicy();
        _options.Policies["terminal"] = new SequencePolicy { Mode = SequenceMode.Reported };

        await _inspector.ListCountersAsync(new SequenceCounterQuery { Mode = SequenceMode.GapFree }, AbortToken);

        var selection = _listed!.Filter.Selection!;
        selection.Names.Should().BeEquivalentTo("invoice");
        selection.NamePartitions.Should().BeEmpty();
        selection.IncludeUnregisteredNames.Should().BeFalse();
        selection.RegisteredNames.Should().BeEquivalentTo("invoice", "receipt", "terminal");
    }

    [Fact]
    public async Task should_select_the_fast_names_and_the_unregistered_names_when_the_mode_is_fast_and_the_default_is_fast()
    {
        _options.Policies["invoice"] = new SequencePolicy { Mode = SequenceMode.GapFree };
        _options.Policies["receipt"] = new SequencePolicy();

        await _inspector.ListCountersAsync(new SequenceCounterQuery { Mode = SequenceMode.Fast }, AbortToken);

        var selection = _listed!.Filter.Selection!;
        selection.Names.Should().BeEquivalentTo("receipt");
        selection.IncludeUnregisteredNames.Should().BeTrue();
        selection.UnregisteredNamePartition.Should().BeNull();
    }

    [Fact]
    public async Task should_select_every_row_when_neither_the_mode_nor_the_current_partition_filters()
    {
        _options.Policies["invoice"] = new SequencePolicy { Mode = SequenceMode.GapFree };

        await _inspector.ListCountersAsync(new SequenceCounterQuery(), AbortToken);

        _listed!.Filter.Selection.Should().BeNull();
    }

    [Fact]
    public async Task should_select_the_current_partition_of_resetting_names_and_every_partition_of_the_others()
    {
        _options.Policies["monthly"] = new SequencePolicy { Reset = SequenceReset.Month };
        _options.Policies["terminal"] = new SequencePolicy { Mode = SequenceMode.Reported };
        _options.DefaultPolicy = new SequencePolicy { Reset = SequenceReset.Year };

        await _inspector.ListCountersAsync(new SequenceCounterQuery { CurrentPartitionsOnly = true }, AbortToken);

        var selection = _listed!.Filter.Selection!;
        selection.NamePartitions.Should().BeEquivalentTo([new SequenceNamePartition("monthly", "2026-10")]);
        selection.Names.Should().BeEquivalentTo("terminal");
        selection.IncludeUnregisteredNames.Should().BeTrue();
        selection.UnregisteredNamePartition.Should().Be("2026");
    }

    [Fact]
    public async Task should_pass_the_filters_and_a_lowercase_search_to_the_reader_and_read_one_row_past_the_page()
    {
        var after = new SequenceKey("t1", "invoice", "");

        await _inspector.ListCountersAsync(
            new SequenceCounterQuery
            {
                TenantId = "t1",
                Name = "invoice",
                Search = "INV",
                After = after,
                Limit = 3,
            },
            AbortToken
        );

        _listed!.Filter.TenantId.Should().Be("t1");
        _listed.Filter.Name.Should().Be("invoice");
        _listed.Filter.Search.Should().Be("INV", "the reader folds case itself");
        _listed.After.Should().Be(after);
        _listed.Limit.Should().Be(4);
    }

    [Fact]
    public async Task should_return_the_last_key_as_next_only_when_the_reader_found_more_rows_than_the_page()
    {
        _Rows(_Record("a"), _Record("b"), _Record("c"));

        var full = await _inspector.ListCountersAsync(new SequenceCounterQuery { Limit = 2 }, AbortToken);
        var last = await _inspector.ListCountersAsync(new SequenceCounterQuery { Limit = 3 }, AbortToken);

        full.Items.Select(static c => c.Key.Name).Should().Equal("a", "b");
        full.Next.Should().Be(new SequenceKey("", "b", ""));
        last.Items.Should().HaveCount(3);
        last.Next.Should().BeNull();
    }

    [Fact]
    public async Task should_format_the_last_number_from_the_value_and_the_updated_date()
    {
        var policy = new SequencePolicy
        {
            Mode = SequenceMode.GapFree,
            Format = "REC-{yyyy}-{seq:D6}",
            Reset = SequenceReset.Year,
        };
        _options.Policies["receipt"] = policy;
        _Rows(
            _Record("receipt", "2026", value: 42, updatedAt: new DateTimeOffset(2026, 3, 1, 10, 0, 0, TimeSpan.Zero))
        );

        var counter = (await _inspector.ListCountersAsync(new SequenceCounterQuery(), AbortToken)).Items.Single();

        counter.LastNumber.Should().Be("REC-2026-000042");
        counter.Policy.Should().BeSameAs(policy);
        counter.Value.Should().Be(42);
        counter.IsCurrentPartition.Should().BeTrue();
        counter.NextResetAt.Should().Be(new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task should_format_with_the_partition_year_when_the_database_clock_wrote_past_the_period_end()
    {
        _options.Policies["receipt"] = new SequencePolicy
        {
            Format = "REC-{yyyy}-{seq:D6}",
            Reset = SequenceReset.Year,
        };
        _Rows(_Record("receipt", "2025", value: 7, updatedAt: new DateTimeOffset(2026, 1, 1, 0, 0, 5, TimeSpan.Zero)));

        var counter = (await _inspector.ListCountersAsync(new SequenceCounterQuery(), AbortToken)).Items.Single();

        counter.LastNumber.Should().Be("REC-2025-000007");
        counter.IsCurrentPartition.Should().BeFalse();
    }

    [Fact]
    public async Task should_format_with_the_fiscal_year_start_when_the_updated_date_left_the_fiscal_year()
    {
        _options.Policies["bill"] = new SequencePolicy
        {
            Format = "B-{fy}-{yyyy}{MM}-{seq}",
            Reset = SequenceReset.FiscalYear,
            FiscalYearStartMonth = 7,
        };
        _Rows(_Record("bill", "FY2024", value: 3, updatedAt: new DateTimeOffset(2025, 7, 1, 0, 0, 1, TimeSpan.Zero)));

        var counter = (await _inspector.ListCountersAsync(new SequenceCounterQuery(), AbortToken)).Items.Single();

        counter.LastNumber.Should().Be("B-2024-202407-3");
    }

    [Fact]
    public async Task should_give_no_last_number_to_a_reported_counter_or_a_policy_without_a_format()
    {
        _options.Policies["terminal"] = new SequencePolicy { Mode = SequenceMode.Reported, Format = "T-{seq}" };
        _Rows(_Record("terminal", "device-1"), _Record("plain"));

        var counters = (await _inspector.ListCountersAsync(new SequenceCounterQuery(), AbortToken)).Items;

        counters.Should().OnlyContain(static c => c.LastNumber == null);
        counters.Should().OnlyContain(static c => c.IsCurrentPartition && c.NextResetAt == null);
    }

    [Fact]
    public async Task should_mark_only_this_months_partition_of_a_monthly_counter_current()
    {
        _options.Policies["monthly"] = new SequencePolicy { Reset = SequenceReset.Month };
        _Rows(_Record("monthly", "2026-09"), _Record("monthly", "2026-10"), _Record("monthly", "device-1"));

        var counters = (await _inspector.ListCountersAsync(new SequenceCounterQuery(), AbortToken)).Items;

        counters.Select(static c => c.IsCurrentPartition).Should().Equal(false, true, false);
    }

    [Fact]
    public async Task should_list_partitions_newest_first_and_continue_before_the_last_one()
    {
        _options.Policies["monthly"] = new SequencePolicy { Reset = SequenceReset.Month };
        SequencePartitionReadRequest? read = null;
        _reader
            .ListPartitionsAsync(Arg.Do<SequencePartitionReadRequest>(r => read = r), Arg.Any<CancellationToken>())
            .Returns(
                new ValueTask<IReadOnlyList<SequenceCounterRecord>>([
                    _Record("monthly", "2026-10", tenantId: "t1"),
                    _Record("monthly", "2026-09", tenantId: "t1"),
                ])
            );

        var page = await _inspector.ListPartitionsAsync(
            new SequencePartitionQuery
            {
                TenantId = "t1",
                Name = "monthly",
                Before = "2026-11",
                Limit = 1,
            },
            AbortToken
        );

        read.Should()
            .Be(
                new SequencePartitionReadRequest
                {
                    TenantId = "t1",
                    Name = "monthly",
                    Before = "2026-11",
                    Limit = 2,
                }
            );
        page.Items.Should().ContainSingle().Which.IsCurrentPartition.Should().BeTrue();
        page.Next.Should().Be("2026-10");
    }

    [Fact]
    public async Task should_count_by_the_mode_names_and_report_the_earliest_next_reset()
    {
        _options.Policies["invoice"] = new SequencePolicy { Mode = SequenceMode.GapFree, Reset = SequenceReset.Year };
        _options.Policies["daily"] = new SequencePolicy { Reset = SequenceReset.Day };
        _options.Policies["terminal"] = new SequencePolicy { Mode = SequenceMode.Reported };
        _reader
            .CountAsync(Arg.Do<SequenceCounterCountRequest>(r => _counted = r), Arg.Any<CancellationToken>())
            .Returns(new SequenceCounterCounts(10_000, 9_000, 600, 400, Capped: true));

        var summary = await _inspector.GetSummaryAsync(new SequenceSummaryQuery { TenantId = "t1" }, AbortToken);

        _counted!.Cap.Should().Be(10_000);
        _counted.Filter.TenantId.Should().Be("t1");
        _counted.Modes.Fast.Should().BeEquivalentTo("daily");
        _counted.Modes.GapFree.Should().BeEquivalentTo("invoice");
        _counted.Modes.Reported.Should().BeEquivalentTo("terminal");
        _counted.Modes.DefaultMode.Should().Be(SequenceMode.Fast);
        summary
            .Should()
            .Be(
                new SequenceCounterSummary
                {
                    Total = 10_000,
                    Fast = 9_000,
                    GapFree = 600,
                    Reported = 400,
                    Capped = true,
                    NextResetAt = new DateTimeOffset(2026, 10, 9, 0, 0, 0, TimeSpan.Zero),
                }
            );
    }

    [Fact]
    public async Task should_report_no_next_reset_in_the_summary_when_no_policy_resets()
    {
        var summary = await _inspector.GetSummaryAsync(new SequenceSummaryQuery(), AbortToken);

        summary.NextResetAt.Should().BeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(201)]
    public async Task should_reject_a_page_size_outside_one_to_two_hundred(int limit)
    {
        var counters = async () =>
            await _inspector.ListCountersAsync(new SequenceCounterQuery { Limit = limit }, AbortToken);
        var partitions = async () =>
            await _inspector.ListPartitionsAsync(new SequencePartitionQuery { Name = "n", Limit = limit }, AbortToken);

        await counters.Should().ThrowAsync<ArgumentOutOfRangeException>();
        await partitions.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(" ", null, null)]
    [InlineData(" t1", null, null)]
    [InlineData(null, " ", null)]
    [InlineData(null, null, "s")]
    public async Task should_reject_an_invalid_tenant_name_or_search_before_reading(
        string? tenantId,
        string? name,
        string? searchSeed
    )
    {
        var search = searchSeed is null ? null : new string('s', SequenceFieldLimits.NameMaxLength + 1);

        var act = async () =>
            await _inspector.ListCountersAsync(
                new SequenceCounterQuery
                {
                    TenantId = tenantId,
                    Name = name,
                    Search = search,
                },
                AbortToken
            );

        await act.Should().ThrowAsync<ArgumentException>();
        await _reader.DidNotReceiveWithAnyArgs().ListAsync(default!, AbortToken);
    }

    [Fact]
    public async Task should_accept_the_empty_tenant_as_the_host_scope_and_reject_an_invalid_cursor_partition()
    {
        await _inspector.ListCountersAsync(new SequenceCounterQuery { TenantId = "" }, AbortToken);
        _listed!.Filter.TenantId.Should().Be("");

        var badCursor = async () =>
            await _inspector.ListCountersAsync(
                new SequenceCounterQuery { After = new SequenceKey("", "n", " ") },
                AbortToken
            );
        var badBefore = async () =>
            await _inspector.ListPartitionsAsync(new SequencePartitionQuery { Name = "n", Before = " " }, AbortToken);

        await badCursor.Should().ThrowAsync<ArgumentException>();
        await badBefore.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public void should_depend_on_no_numbering_service_and_not_on_the_current_tenant()
    {
        var dependencies = typeof(SequenceInspector)
            .GetConstructors()
            .SelectMany(static c => c.GetParameters())
            .Select(static p => p.ParameterType);

        dependencies
            .Should()
            .NotContain(
                [
                    typeof(ISequenceStore),
                    typeof(ISequenceGenerator),
                    typeof(IUnitOfWorkSequences),
                    typeof(ICurrentTenant),
                ],
                "inspection reads every tenant, whatever tenant the caller runs as"
            );
    }

    private void _Rows(params SequenceCounterRecord[] rows)
    {
        _reader
            .ListAsync(Arg.Do<SequenceCounterReadRequest>(r => _listed = r), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<IReadOnlyList<SequenceCounterRecord>>(rows));
    }

    private static SequenceCounterRecord _Record(
        string name,
        string partition = "",
        long value = 1,
        DateTimeOffset? updatedAt = null,
        string tenantId = ""
    )
    {
        return new SequenceCounterRecord(new SequenceKey(tenantId, name, partition), value, updatedAt ?? _Now);
    }
}
