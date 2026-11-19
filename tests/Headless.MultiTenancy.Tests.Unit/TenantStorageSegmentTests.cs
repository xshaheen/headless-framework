// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.MultiTenancy;

namespace Tests;

public sealed class TenantStorageSegmentTests
{
    [Theory]
    [InlineData("acme")]
    [InlineData("42")]
    [InlineData("a")]
    [InlineData("acme_corp.eu")]
    [InlineData("0190c7a2-7a5e-7b3c-9f2e-1d4c5b6a7e8f")]
    public void should_accept_an_id_of_lowercase_letters_digits_and_inner_punctuation(string tenantId)
    {
        TenantStorageSegment.IsValid(tenantId).Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("Acme")]
    [InlineData("0190C7A2-7A5E-7B3C-9F2E-1D4C5B6A7E8F")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("a:b")]
    [InlineData("a b")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData(".hidden")]
    [InlineData("trailing.")]
    [InlineData("-acme")]
    [InlineData("acme_")]
    [InlineData("é")]
    [InlineData("acme\n")]
    public void should_refuse_an_id_that_is_not_one_safe_path_segment(string? tenantId)
    {
        TenantStorageSegment.IsValid(tenantId).Should().BeFalse();
    }
}
