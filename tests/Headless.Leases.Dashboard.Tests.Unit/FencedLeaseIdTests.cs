// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Fencing;
using Headless.Leases;
using Headless.Testing.Tests;

namespace Tests;

public sealed class FencedLeaseIdTests : TestBase
{
    [Theory]
    [InlineData("acme", "ledger-export", "export/2026-11-03")]
    [InlineData("acme", "ledger-export", "100% done")]
    [InlineData(null, "cleanup", "batch/7")]
    [InlineData("tenant/with/slashes", "kind%x", "a b/c")]
    public void should_round_trip_every_key_part(string? tenantId, string kind, string resource)
    {
        // when
        var id = FencedLeaseId.Format(tenantId, kind, resource);
        var parsed = FencedLeaseId.TryParse(id, out var parsedTenant, out var parsedKind, out var parsedResource);

        // then
        parsed.Should().BeTrue();
        parsedTenant.Should().Be(tenantId);
        parsedKind.Should().Be(kind);
        parsedResource.Should().Be(resource);
    }

    [Fact]
    public void should_mint_the_host_scope_as_an_empty_first_segment()
    {
        FencedLeaseId.Format(null, "cleanup", "batch/7").Should().Be("/cleanup/batch%2F7");
    }

    [Theory]
    [InlineData("")]
    [InlineData("a/b")]
    [InlineData("a/b/c/d")]
    [InlineData("acme//resource")]
    [InlineData("acme/kind/")]
    public void should_refuse_any_other_shape(string id)
    {
        FencedLeaseId.TryParse(id, out _, out _, out _).Should().BeFalse();
    }

    [Fact]
    public void should_round_trip_a_list_cursor_and_refuse_text_it_did_not_write()
    {
        // given
        var position = new FencedLeasePosition(null, "cleanup", "batch/7");

        // when
        var cursor = FencedLeaseCursor.Encode(position);

        // then
        FencedLeaseCursor.TryDecode(cursor, out var decoded).Should().BeTrue();
        decoded.Should().Be(position);
        FencedLeaseCursor.TryDecode("garbage", out _).Should().BeFalse();
        FencedLeaseCursor.TryDecode("v1.!!!", out _).Should().BeFalse();
        FencedLeaseCursor.TryDecode("v1.WyJhIl0", out _).Should().BeFalse();
    }
}
