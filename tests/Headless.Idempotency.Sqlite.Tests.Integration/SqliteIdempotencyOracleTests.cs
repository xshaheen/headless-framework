// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

[Collection<SqliteIdempotencyFixture>]
public sealed class SqliteIdempotencyOracleTests(SqliteIdempotencyFixture fixture)
    : IdempotencyOracleTests<SqliteIdempotencyFixture>(fixture)
{
    // The SQLite clock steps in whole milliseconds; 2 ms guarantees two writes never share a clock value.
    protected override TimeSpan ClockSpacing => TimeSpan.FromMilliseconds(2);

    [Fact]
    public override Task should_match_the_in_memory_model_on_generated_histories()
    {
        return base.should_match_the_in_memory_model_on_generated_histories();
    }

    [Fact]
    public override Task should_match_the_in_memory_model_on_ordinary_keys()
    {
        return base.should_match_the_in_memory_model_on_ordinary_keys();
    }
}
