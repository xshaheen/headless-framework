// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

public partial class SqlServerStorageTests
{
    [Fact]
    public override Task should_scope_a_published_operation_to_the_rows_a_search_matches() =>
        base.should_scope_a_published_operation_to_the_rows_a_search_matches();

    [Fact]
    public override Task should_scope_an_inbox_operation_to_the_generations_a_content_search_matches() =>
        base.should_scope_an_inbox_operation_to_the_generations_a_content_search_matches();
}
