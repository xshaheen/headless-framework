// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

[Collection<SqlServerIdempotencyFixture>]
public sealed class SqlServerIdempotencyOracleTests(SqlServerIdempotencyFixture fixture)
    : IdempotencyOracleTests<SqlServerIdempotencyFixture>(fixture)
{
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
