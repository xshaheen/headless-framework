// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Headless.Domain;
using Headless.MultiTenancy;

namespace Tests;

public sealed class TenantRecordTests
{
    [Fact]
    public void should_derive_lowercase_normalized_identifier_from_mixed_case_identifier_at_construction()
    {
        // given & when
        var record = new TenantRecord("ten_1", "Acme");

        // then
        record.Identifier.Should().Be("Acme");
        record.NormalizedIdentifier.Should().Be("acme");
    }

    [Fact]
    public void should_trim_and_lowercase_normalized_identifier_while_preserving_raw_identifier()
    {
        // given & when
        var record = new TenantRecord("ten_1", "  Acme  ");

        // then
        record.Identifier.Should().Be("  Acme  ");
        record.NormalizedIdentifier.Should().Be("acme");
    }

    [Fact]
    public void should_recompute_normalized_identifier_when_identifier_changes_via_set_identifier()
    {
        // given - a rebrand: the tenant's public identifier changes after creation
        var record = new TenantRecord("ten_1", "Acme");

        // when
        record.SetIdentifier("NewAcme");

        // then
        record.Identifier.Should().Be("NewAcme");
        record.NormalizedIdentifier.Should().Be("newacme");
    }

    [Fact]
    public void should_not_expose_a_public_setter_for_normalized_identifier()
    {
        // given
        var property = typeof(TenantRecord).GetProperty(
            nameof(TenantRecord.NormalizedIdentifier),
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly
        );

        // then - NormalizedIdentifier is derived-only; SetIdentifier is the sole mutation path
        property.Should().NotBeNull();
        var setter = property!.SetMethod;
        var hasPublicSetter = setter?.IsPublic ?? false;
        hasPublicSetter.Should().BeFalse();
    }

    [Fact]
    public void should_not_implement_i_multi_tenant()
    {
        // then - the tenant catalog sits outside the EF tenant query filter by construction
        typeof(TenantRecord).Should().NotBeAssignableTo<IMultiTenant>();
    }

    [Fact]
    public void should_default_is_enabled_to_true()
    {
        // given & when
        var record = new TenantRecord("ten_1", "acme");

        // then
        record.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void should_start_with_an_empty_extra_properties_bag()
    {
        // given & when
        var record = new TenantRecord("ten_1", "acme");

        // then
        record.ExtraProperties.Should().BeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void should_throw_when_identifier_is_null_or_white_space(string? identifier)
    {
        // given & when
        var act = () => new TenantRecord("ten_1", identifier!);

        // then
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void should_throw_when_id_is_null_or_white_space(string? id)
    {
        // given & when
        var act = () => new TenantRecord(id!, "acme");

        // then
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void should_throw_when_set_identifier_is_called_with_null_or_white_space(string? identifier)
    {
        // given
        var record = new TenantRecord("ten_1", "acme");

        // when
        var act = () => record.SetIdentifier(identifier!);

        // then
        act.Should().Throw<ArgumentException>();
    }
}
