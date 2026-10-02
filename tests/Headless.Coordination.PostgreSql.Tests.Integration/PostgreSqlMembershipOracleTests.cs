// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

[Collection<PostgreSqlMembershipFixture>]
public sealed class PostgreSqlMembershipOracleTests(PostgreSqlMembershipFixture fixture)
    : MembershipOracleTests<PostgreSqlMembershipFixture>(fixture)
{
    [Fact]
    public override void should_generate_the_same_history_for_one_seed()
    {
        base.should_generate_the_same_history_for_one_seed();
    }

    [Fact]
    public override Task should_match_the_model_at_every_step_of_generated_histories()
    {
        return base.should_match_the_model_at_every_step_of_generated_histories();
    }
}
