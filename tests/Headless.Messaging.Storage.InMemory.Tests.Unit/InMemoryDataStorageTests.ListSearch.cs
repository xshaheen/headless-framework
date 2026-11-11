// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

public sealed partial class InMemoryDataStorageTests
{
    [Fact]
    public override Task should_filter_a_list_by_added_time_with_an_exclusive_end() =>
        base.should_filter_a_list_by_added_time_with_an_exclusive_end();

    [Fact]
    public override Task should_page_a_name_sorted_list_by_name_then_id_without_gaps_or_repeats() =>
        base.should_page_a_name_sorted_list_by_name_then_id_without_gaps_or_repeats();

    [Fact]
    public override Task should_search_content_as_a_literal_substring_without_regard_to_case() =>
        base.should_search_content_as_a_literal_substring_without_regard_to_case();

    [Fact]
    public override Task should_find_a_correlation_id_exactly_and_not_a_body_that_merely_contains_it() =>
        base.should_find_a_correlation_id_exactly_and_not_a_body_that_merely_contains_it();

    [Fact]
    public override Task should_filter_a_list_by_lane() => base.should_filter_a_list_by_lane();
}
