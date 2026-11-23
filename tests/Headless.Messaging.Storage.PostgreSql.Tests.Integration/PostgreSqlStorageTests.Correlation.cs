// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

public partial class PostgreSqlStorageTests
{
    [Fact]
    public override Task should_match_a_correlation_id_ordinally_in_the_list_filter() =>
        base.should_match_a_correlation_id_ordinally_in_the_list_filter();

    [Fact]
    public override Task should_trace_a_correlation_id_ordinally() => base.should_trace_a_correlation_id_ordinally();

    [Fact]
    public override Task should_trace_a_published_message_and_every_generation_delivered_from_it() =>
        base.should_trace_a_published_message_and_every_generation_delivered_from_it();

    [Fact]
    public override Task should_trace_the_oldest_rows_first_and_one_past_the_limit() =>
        base.should_trace_the_oldest_rows_first_and_one_past_the_limit();

    [Fact]
    public override Task should_not_trace_a_correlation_id_longer_than_the_column_and_still_store_the_message() =>
        base.should_not_trace_a_correlation_id_longer_than_the_column_and_still_store_the_message();

    [Fact]
    public override Task should_not_find_a_correlation_id_that_only_the_body_holds() =>
        base.should_not_find_a_correlation_id_that_only_the_body_holds();
}
