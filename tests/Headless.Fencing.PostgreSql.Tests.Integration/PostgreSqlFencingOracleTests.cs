// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

[Collection<PostgreSqlFencingFixture>]
public sealed class PostgreSqlFencingOracleTests(PostgreSqlFencingFixture fixture)
    : FencingOracleTests<PostgreSqlFencingFixture>(fixture)
{
    // PostgreSQL stores microseconds, so a lease duration is compared to the model within one.
    protected override long PrecisionTicks => 10;

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
