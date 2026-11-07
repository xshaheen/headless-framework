// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

public partial class PostgreSqlStorageTests
{
    [Fact]
    public override Task should_page_a_keyset_list_without_gaps_or_repeats_while_rows_change_between_pages() =>
        base.should_page_a_keyset_list_without_gaps_or_repeats_while_rows_change_between_pages();

    [Fact]
    public override Task should_return_the_cap_and_capped_when_a_count_exceeds_the_cap() =>
        base.should_return_the_cap_and_capped_when_a_count_exceeds_the_cap();

    [Fact]
    public override Task should_group_terminal_failures_by_fingerprint_and_consumer_bytes() =>
        base.should_group_terminal_failures_by_fingerprint_and_consumer_bytes();

    [Fact]
    public override Task should_group_published_failures_by_fingerprint_without_a_consumer() =>
        base.should_group_published_failures_by_fingerprint_without_a_consumer();

    [Fact]
    public override Task should_freeze_an_operation_scope_at_its_high_water_mark_and_never_re_execute_a_message_twice() =>
        base.should_freeze_an_operation_scope_at_its_high_water_mark_and_never_re_execute_a_message_twice();

    [Fact]
    public override Task should_refuse_a_chunk_write_over_a_generation_another_node_already_re_executed() =>
        base.should_refuse_a_chunk_write_over_a_generation_another_node_already_re_executed();

    [Fact]
    public override Task should_leave_a_held_generation_out_of_a_delete_scope() =>
        base.should_leave_a_held_generation_out_of_a_delete_scope();

    [Fact]
    public override Task should_read_the_ledger_newest_first_filtered_by_actor_including_refused_interventions() =>
        base.should_read_the_ledger_newest_first_filtered_by_actor_including_refused_interventions();

    [Fact]
    public override Task should_accept_an_empty_reason_for_a_single_hold_and_store_it_as_null() =>
        base.should_accept_an_empty_reason_for_a_single_hold_and_store_it_as_null();

    [Fact]
    public override Task should_serve_reads_to_a_viewer_without_a_named_operator_and_refuse_its_writes() =>
        base.should_serve_reads_to_a_viewer_without_a_named_operator_and_refuse_its_writes();
}
