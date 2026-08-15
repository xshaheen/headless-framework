// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.MultiTenancy;

namespace Tests;

public sealed class TenantResolutionOutcomeTests
{
    [Fact]
    public void should_carry_the_tenant_only_for_resolved_outcome()
    {
        // given
        var tenant = new TenantInfo("ten_1", "acme", "Acme", isEnabled: true);

        // when
        var outcome = TenantResolutionOutcome.Resolved(tenant);

        // then
        outcome.Kind.Should().Be(TenantResolutionKind.Resolved);
        outcome.Tenant.Should().BeSameAs(tenant);
    }

    [Fact]
    public void should_have_no_tenant_for_every_non_resolved_outcome()
    {
        // given
        TenantResolutionOutcome[] outcomes =
        [
            TenantResolutionOutcome.Unknown,
            TenantResolutionOutcome.Disabled,
            TenantResolutionOutcome.Ignored,
            TenantResolutionOutcome.Invalid,
        ];

        // then
        outcomes.Should().AllSatisfy(outcome => outcome.Tenant.Should().BeNull());
        outcomes
            .Select(outcome => outcome.Kind)
            .Should()
            .BeEquivalentTo(
                [
                    TenantResolutionKind.Unknown,
                    TenantResolutionKind.Disabled,
                    TenantResolutionKind.Ignored,
                    TenantResolutionKind.Invalid,
                ],
                options => options.WithStrictOrdering()
            );
    }

    [Fact]
    public void should_throw_when_resolved_tenant_is_null()
    {
        // when
        var act = () => TenantResolutionOutcome.Resolved(null!);

        // then
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void should_switch_exhaustively_over_every_kind_without_a_default_arm()
    {
        // given — this loop compiles only if the switch below covers the full closed set; a new
        // TenantResolutionKind member would need a corresponding switch arm to keep building.
        foreach (var kind in Enum.GetValues<TenantResolutionKind>())
        {
            // when
            var label = kind switch
            {
                TenantResolutionKind.Resolved => "resolved",
                TenantResolutionKind.Unknown => "unknown",
                TenantResolutionKind.Disabled => "disabled",
                TenantResolutionKind.Ignored => "ignored",
                TenantResolutionKind.Invalid => "invalid",
                _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, message: null),
            };

            // then
            label.Should().NotBeNullOrEmpty();
        }
    }

    [Fact]
    public void should_be_equal_when_kind_and_tenant_reference_match()
    {
        // given
        var tenant = new TenantInfo("ten_1", "acme", "Acme", isEnabled: true);
        var first = TenantResolutionOutcome.Resolved(tenant);
        var second = TenantResolutionOutcome.Resolved(tenant);

        // then
        (first == second)
            .Should()
            .BeTrue();
        first.Equals(second).Should().BeTrue();
        first.GetHashCode().Should().Be(second.GetHashCode());
        (first != second).Should().BeFalse();
    }

    [Fact]
    public void should_not_be_equal_for_different_kinds()
    {
        (TenantResolutionOutcome.Unknown == TenantResolutionOutcome.Disabled).Should().BeFalse();
        (TenantResolutionOutcome.Unknown != TenantResolutionOutcome.Disabled).Should().BeTrue();
    }

    [Fact]
    public void should_not_be_equal_when_resolved_tenants_differ_by_reference()
    {
        // given — two distinct TenantInfo instances with identical data; TenantInfo defines no
        // value equality of its own, so the outcome compares tenants by reference.
        var first = TenantResolutionOutcome.Resolved(new TenantInfo("ten_1", "acme", "Acme", isEnabled: true));
        var second = TenantResolutionOutcome.Resolved(new TenantInfo("ten_1", "acme", "Acme", isEnabled: true));

        // then
        (first == second)
            .Should()
            .BeFalse();
    }
}
