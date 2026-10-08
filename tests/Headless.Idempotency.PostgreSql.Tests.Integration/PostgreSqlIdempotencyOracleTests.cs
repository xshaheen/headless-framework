// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

[Collection<PostgreSqlIdempotencyFixture>]
public sealed class PostgreSqlIdempotencyOracleTests(PostgreSqlIdempotencyFixture fixture)
    : IdempotencyOracleTests<PostgreSqlIdempotencyFixture>(fixture)
{
    // clock_timestamp() has microsecond resolution, far finer than one round trip, so a 1 ms gap is ample.
    protected override TimeSpan ClockSpacing => TimeSpan.FromMilliseconds(1);

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
