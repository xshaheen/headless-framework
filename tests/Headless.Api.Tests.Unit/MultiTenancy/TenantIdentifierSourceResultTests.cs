// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api.MultiTenancy;
using Headless.Testing.Tests;

namespace Tests.MultiTenancy;

public sealed class TenantIdentifierSourceResultTests : TestBase
{
    [Fact]
    public void should_treat_default_as_none()
    {
        // None is the zero value so an uninitialized result never masquerades as found.
        var result = default(TenantIdentifierSourceResult);

        result.Kind.Should().Be(TenantIdentifierSourceResultKind.None);
        result.Identifier.Should().BeNull();
        result.Should().Be(TenantIdentifierSourceResult.None);
    }

    [Fact]
    public void should_normalize_null_to_none_when_found()
    {
        var result = TenantIdentifierSourceResult.Found(null);

        result.Kind.Should().Be(TenantIdentifierSourceResultKind.None);
        result.Identifier.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(" \t\r\n ")]
    public void should_normalize_whitespace_to_none_when_found(string identifier)
    {
        var result = TenantIdentifierSourceResult.Found(identifier);

        result.Kind.Should().Be(TenantIdentifierSourceResultKind.None);
        result.Identifier.Should().BeNull();
    }

    [Fact]
    public void should_carry_the_identifier_unchanged_when_found()
    {
        // Sources never trim, lowercase, or shape-validate — the raw value passes through as-is,
        // including mixed case and inner whitespace a source might legitimately capture.
        var result = TenantIdentifierSourceResult.Found("  Acme Corp ");

        result.Kind.Should().Be(TenantIdentifierSourceResultKind.Found);
        result.Identifier.Should().Be("  Acme Corp ");
    }

    [Fact]
    public void should_carry_no_identifier_when_invalid()
    {
        var result = TenantIdentifierSourceResult.Invalid;

        result.Kind.Should().Be(TenantIdentifierSourceResultKind.Invalid);
        result.Identifier.Should().BeNull();
    }

    [Fact]
    public void should_distinguish_none_found_and_invalid_by_equality()
    {
        // The middleware and consumers pattern-match on Kind, but record equality must still partition
        // the three states correctly — a None/Found mix-up is exactly the "blank found" case the
        // null/whitespace-normalization rule above removes.
        TenantIdentifierSourceResult.None.Should().Be(TenantIdentifierSourceResult.Found(null));
        TenantIdentifierSourceResult.None.Should().NotBe(TenantIdentifierSourceResult.Invalid);
        TenantIdentifierSourceResult.Found("acme").Should().NotBe(TenantIdentifierSourceResult.Found("globex"));
        TenantIdentifierSourceResult.Found("acme").Should().Be(TenantIdentifierSourceResult.Found("acme"));
    }
}
