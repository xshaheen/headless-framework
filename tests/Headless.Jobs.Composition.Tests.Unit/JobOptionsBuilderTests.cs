// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs;
using Headless.Jobs.Enums;
using Headless.Jobs.Models;
using Headless.Testing.Tests;

namespace Tests;

public sealed class JobOptionsBuilderTests : TestBase
{
    [Fact]
    public void empty_builder_preserves_canonical_defaults()
    {
        var builder = new JobOptionsBuilder();
        builder.Build().Should().Be(new JobOptions());
        builder.Build().Should().NotBeSameAs(builder.Build());
    }

    [Fact]
    public void retry_arrays_are_copied_on_input_and_for_each_snapshot()
    {
        var intervals = new[] { 2, 5 };
        var builder = new JobOptionsBuilder().WithRetryIntervals(intervals);
        intervals[0] = 99;
        var first = builder.Build();
        var second = builder.Build();
        first.RetryIntervals.Should().Equal(2, 5);
        first.RetryIntervals![0] = 7;
        second.RetryIntervals.Should().Equal(2, 5);
        builder.Build().RetryIntervals.Should().Equal(2, 5);
        builder.WithRetryIntervals(10);
        second.RetryIntervals.Should().Equal(2, 5);
        builder.Build().RetryIntervals.Should().Equal(10);
    }

    [Fact]
    public void building_preserves_invalid_values_for_the_existing_scheduling_validator()
    {
        var options = new JobOptionsBuilder()
            .WithRetries(-1)
            .WithRetryIntervals(-2)
            .WithNodeDeathPolicy((NodeDeathPolicy)999)
            .Build();
        options.Retries.Should().Be(-1);
        options.RetryIntervals.Should().Equal(-2);
        options.OnNodeDeath.Should().Be((NodeDeathPolicy)999);
    }

    [Fact]
    public void nullable_setters_restore_inheritance_while_boolean_assertions_survive_reuse()
    {
        var builder = new JobOptionsBuilder()
            .WithRetries(0)
            .WithRetryIntervals()
            .WithNodeDeathPolicy(NodeDeathPolicy.MarkFailed)
            .WithCorrelationId("correlation")
            .WithCausationId("cause")
            .WithDescription("description")
            .WithTenantId("tenant")
            .RequireAtomicEnlistment()
            .AsSystemJob();
        var explicitOptions = builder.Build();
        explicitOptions.Retries.Should().Be(0);
        explicitOptions.RetryIntervals.Should().NotBeNull().And.BeEmpty();
        builder
            .WithRetries(null)
            .WithRetryIntervals(null)
            .WithNodeDeathPolicy(null)
            .WithCorrelationId(null)
            .WithCausationId(null)
            .WithDescription(null)
            .WithTenantId(null);
        builder.Build().Should().Be(new JobOptions { RequireAtomicEnlistment = true, IsSystemJob = true });
        explicitOptions.OnNodeDeath.Should().Be(NodeDeathPolicy.MarkFailed);
        explicitOptions.TenantId.Should().Be("tenant");
    }
}
