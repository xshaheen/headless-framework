// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

[Collection<SqlServerFencingFixture>]
public sealed class SqlServerFencingOracleTests(SqlServerFencingFixture fixture)
    : FencingOracleTests<SqlServerFencingFixture>(fixture)
{
    // datetimeoffset(7) keeps 100-nanosecond ticks, the model's own precision, so durations compare exactly.
    protected override long PrecisionTicks => 1;

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
