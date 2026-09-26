// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Fencing;
using Headless.Testing.Tests;

namespace Tests;

public sealed class LeaseRequestResolverTests : TestBase
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(" job")]
    [InlineData("job ")]
    public void should_reject_a_blank_or_padded_kind(string kind)
    {
        var resolver = new FencingTestContext().Resolver;

        var act = () => resolver.Resolve(kind, "order-1");

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(" order-1")]
    [InlineData("order-1 ")]
    [InlineData("order-1\t")]
    public void should_reject_a_blank_or_padded_resource(string resource)
    {
        var resolver = new FencingTestContext().Resolver;

        var act = () => resolver.Resolve("job", resource);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void should_reject_a_null_kind_or_resource()
    {
        var resolver = new FencingTestContext().Resolver;

        resolver.Invoking(r => r.Resolve(null!, "order-1")).Should().Throw<ArgumentNullException>();
        resolver.Invoking(r => r.Resolve("job", null!)).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void should_accept_a_kind_and_resource_exactly_at_their_limits()
    {
        // given
        var resolver = new FencingTestContext().Resolver;
        var kind = new string('k', FencingFieldLimits.KindMaxLength);
        var resource = new string('r', FencingFieldLimits.ResourceMaxLength);

        // when
        var key = resolver.Resolve(kind, resource);

        // then
        key.Should().Be(new LeaseKey("", kind, resource));
    }

    [Fact]
    public void should_reject_a_kind_one_unit_over_its_limit()
    {
        var resolver = new FencingTestContext().Resolver;
        var kind = new string('k', FencingFieldLimits.KindMaxLength + 1);

        var act = () => resolver.Resolve(kind, "order-1");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void should_reject_a_resource_one_unit_over_its_limit()
    {
        var resolver = new FencingTestContext().Resolver;
        var resource = new string('r', FencingFieldLimits.ResourceMaxLength + 1);

        var act = () => resolver.Resolve("job", resource);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void should_map_a_null_tenant_to_the_host_scope()
    {
        var context = new FencingTestContext();
        context.Tenant.Id = null;

        var key = context.Resolver.Resolve("job", "order-1");

        key.Should().Be(new LeaseKey("", "job", "order-1"));
        key.PublicTenantId.Should().BeNull();
    }

    [Fact]
    public void should_read_the_current_tenant_on_every_call()
    {
        // given
        var context = new FencingTestContext();

        // when
        LeaseKey first;
        LeaseKey second;

        using (context.Tenant.Change("t1"))
        {
            first = context.Resolver.Resolve("job", "order-1");
        }

        using (context.Tenant.Change("t2"))
        {
            second = context.Resolver.Resolve("job", "order-1");
        }

        var host = context.Resolver.Resolve("job", "order-1");

        // then
        first.TenantId.Should().Be("t1");
        second.TenantId.Should().Be("t2");
        host.TenantId.Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(" t1")]
    [InlineData("t1 ")]
    public void should_reject_a_blank_or_padded_current_tenant(string tenantId)
    {
        var context = new FencingTestContext();
        context.Tenant.Id = tenantId;

        var act = () => context.Resolver.Resolve("job", "order-1");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void should_accept_a_tenant_at_its_limit_and_reject_one_over()
    {
        var context = new FencingTestContext();

        context.Tenant.Id = new string('t', FencingFieldLimits.TenantIdMaxLength);
        context.Resolver.Resolve("job", "order-1").TenantId.Should().HaveLength(FencingFieldLimits.TenantIdMaxLength);

        context.Tenant.Id = new string('t', FencingFieldLimits.TenantIdMaxLength + 1);
        context.Resolver.Invoking(r => r.Resolve("job", "order-1")).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void should_key_a_lease_by_its_own_tenant_not_the_current_one()
    {
        // given — a lease handed to another context keeps the identity it was granted under
        var context = new FencingTestContext();
        context.Tenant.Id = "t2";

        // when
        var key = LeaseRequestResolver.ResolveLease(new FencedLease("t1", "job", "order-1", 7));
        var hostKey = LeaseRequestResolver.ResolveLease(new FencedLease(null, "job", "order-1", 7));

        // then
        key.Should().Be(new LeaseKey("t1", "job", "order-1"));
        hostKey.Should().Be(new LeaseKey("", "job", "order-1"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void should_reject_a_lease_whose_generation_is_not_positive(long generation)
    {
        var act = () => LeaseRequestResolver.ResolveLease(new FencedLease(null, "job", "order-1", generation));

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(null, " job", "order-1")]
    [InlineData(null, "job", "order-1 ")]
    [InlineData("", "job", "order-1")]
    [InlineData(" t1", "job", "order-1")]
    public void should_reject_a_lease_with_an_invalid_identity(string? tenantId, string kind, string resource)
    {
        var act = () => LeaseRequestResolver.ResolveLease(new FencedLease(tenantId, kind, resource, 1));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void should_reject_a_null_lease()
    {
        var act = () => LeaseRequestResolver.ResolveLease(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void should_accept_durations_at_both_bounds()
    {
        var context = new FencingTestContext();

        context.Resolver.ValidateDuration(context.Options.MinimumLeaseDuration).Should().Be(TimeSpan.FromSeconds(1));
        context.Resolver.ValidateDuration(context.Options.MaximumLeaseDuration).Should().Be(TimeSpan.FromDays(1));
    }

    [Fact]
    public void should_reject_durations_outside_the_bounds()
    {
        var context = new FencingTestContext();

        context
            .Resolver.Invoking(r => r.ValidateDuration(TimeSpan.FromMilliseconds(999)))
            .Should()
            .Throw<ArgumentOutOfRangeException>();
        context
            .Resolver.Invoking(r => r.ValidateDuration(TimeSpan.FromDays(1) + TimeSpan.FromTicks(1)))
            .Should()
            .Throw<ArgumentOutOfRangeException>();
        context.Resolver.Invoking(r => r.ValidateDuration(TimeSpan.Zero)).Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void should_read_the_duration_bounds_on_every_call()
    {
        var context = new FencingTestContext();
        context.Options.MaximumLeaseDuration = TimeSpan.FromMinutes(1);

        var act = () => context.Resolver.ValidateDuration(TimeSpan.FromMinutes(2));

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" job")]
    public void should_reject_an_invalid_kind_for_maintenance_calls(string kind)
    {
        var act = () => LeaseRequestResolver.ResolveKind(kind);

        act.Should().Throw<ArgumentException>();
    }
}
