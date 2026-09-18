// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using Headless.Api.MultiTenancy;
using Headless.Testing.Tests;

namespace Tests.MultiTenancy;

/// <summary>
/// Pins the host template grammar: the documented examples parse and match with the documented
/// captures, invalid templates fail <see cref="HostTemplate.TryParse"/> with a message naming the
/// template and the broken rule (surfaced verbatim by the options validator), and the compiled
/// patterns are exact — one token matches exactly one DNS-style label.
/// </summary>
public sealed class HostTemplateTests : TestBase
{
    [Theory]
    [InlineData("{tenant}.example.com", "acme.example.com", "acme")] // one label before a literal suffix
    [InlineData("{tenant}.example.com", "ACME.EXAMPLE.COM", "ACME")] // matching is case-insensitive, capture keeps input casing
    [InlineData("{tenant}.Example.COM", "acme.example.com", "acme")] // template literals match case-insensitively
    [InlineData("{tenant}.*", "acme.any.thing", "acme")] // trailing wildcard: any suffix
    [InlineData("{tenant}.*", "acme", "acme")] // trailing wildcard matches zero labels
    [InlineData("*.{tenant}.?", "a.b.acme.dev", "acme")] // leading wildcard plus one trailing label
    [InlineData("*.{tenant}.?", "acme.dev", "acme")] // leading wildcard matches zero labels
    [InlineData("*.{tenant}", "a.b.acme", "acme")]
    [InlineData("?.{tenant}.example.com", "eu.acme.example.com", "acme")] // '?' is exactly one label
    [InlineData("{tenant}.?.example.com", "acme.eu.example.com", "acme")]
    [InlineData("*.acme.{tenant}", "x.y.acme.dev", "dev")]
    [InlineData("{tenant}", "orders.acme.com", "orders.acme.com")] // bare token: whole host is the identifier
    [InlineData("{tenant}", "acme", "acme")]
    public void should_match_the_documented_examples(string template, string host, string expectedIdentifier)
    {
        var parsed = HostTemplate.TryParse(template, out var hostTemplate, out var error);

        parsed.Should().BeTrue(error ?? "TryParse failed.");
        hostTemplate.Should().NotBeNull();

        var matched = hostTemplate!.Match(host, out var identifier);

        matched.Should().BeTrue();
        identifier.Should().Be(expectedIdentifier);
    }

    [Fact]
    public void should_match_literal_labels_case_insensitively_under_a_turkish_culture()
    {
        // Hostnames are ASCII case-insensitive regardless of process culture. Without
        // CultureInvariant, IgnoreCase folds 'I'/'i' through the Turkish dotted/dotless forms under
        // tr-TR, so the literal label "api" fails to match "API". CurrentCulture is per-thread/async
        // flow, so this swap does not leak into tests running in parallel.
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;

        try
        {
            var turkish = CultureInfo.GetCultureInfo("tr-TR");
            CultureInfo.CurrentCulture = turkish;
            CultureInfo.CurrentUICulture = turkish;

            var parsed = HostTemplate.TryParse("api.{tenant}.example.com", out var hostTemplate, out var error);

            parsed.Should().BeTrue(error ?? "TryParse failed.");
            hostTemplate.Should().NotBeNull();

            var matched = hostTemplate!.Match("API.ACME.EXAMPLE.COM", out var identifier);

            matched.Should().BeTrue();
            identifier.Should().Be("ACME");
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    [Theory]
    [InlineData("{tenant}.example.com", "a.b.example.com")] // {tenant} is exactly one label
    [InlineData("{tenant}.example.com", "example.com")] // apex has no tenant label
    [InlineData("{tenant}.example.com", "acme.example.org")] // suffix must match exactly
    [InlineData("{tenant}.example.com", "acme.example.com.")] // Match is exact; trailing-dot strip is the source's job
    [InlineData("*.{tenant}.?", "acme")] // needs tenant plus at least one more label
    [InlineData("?.{tenant}.example.com", "acme.example.com")] // '?' never matches zero labels
    public void should_not_match_hosts_outside_the_template_shape(string template, string host)
    {
        var parsed = HostTemplate.TryParse(template, out var hostTemplate, out var error);

        parsed.Should().BeTrue(error ?? "TryParse failed.");

        hostTemplate!.Match(host, out var identifier).Should().BeFalse();
        identifier.Should().BeNull();
    }

    [Theory]
    [InlineData("example.com")] // no {tenant} token
    [InlineData("*.example.com")] // no {tenant} token
    [InlineData("?")] // no {tenant} token
    [InlineData("{tenant}.{tenant}.com")] // two {tenant} tokens
    [InlineData("*.*")] // wildcards only, no {tenant} token
    [InlineData("{tenant}*")] // token adjacent to other text in one label
    [InlineData("a{tenant}")] // token adjacent to other text in one label
    [InlineData("{tenant}.example.com:443")] // port
    [InlineData("acme:example.com")] // port-like colon anywhere
    [InlineData("{tenant}..com")] // empty label
    [InlineData(".{tenant}.com")] // leading dot = empty first label
    [InlineData("{tenant}.com.")] // trailing dot = empty last label
    [InlineData("{tenant}.exa_mple.com")] // literal with a character outside letters/digits/hyphen
    public void should_reject_invalid_templates_with_a_message_naming_the_template_and_rule(string template)
    {
        var parsed = HostTemplate.TryParse(template, out var hostTemplate, out var error);

        parsed.Should().BeFalse();
        hostTemplate.Should().BeNull();
        error.Should().NotBeNullOrWhiteSpace();
        error
            .Should()
            .Contain(
                template,
                "the validator surfaces the parser message verbatim, so it must name the offending template"
            );
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void should_reject_a_blank_template(string template)
    {
        var parsed = HostTemplate.TryParse(template, out var hostTemplate, out var error);

        parsed.Should().BeFalse();
        hostTemplate.Should().BeNull();
        error.Should().NotBeNullOrWhiteSpace().And.Contain("empty");
    }

    [Fact]
    public void should_reject_a_null_template()
    {
        var parsed = HostTemplate.TryParse(null, out var hostTemplate, out var error);

        parsed.Should().BeFalse();
        hostTemplate.Should().BeNull();
        error.Should().NotBeNullOrWhiteSpace().And.Contain("empty");
    }
}
