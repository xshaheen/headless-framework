// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api;
using Headless.Api.MultiTenancy;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Tests.MultiTenancy;

/// <summary>
/// Pins the host source behavior in isolation: port-excluded host read with casing preserved,
/// one trailing dot stripped, IP literals and hosts over 253 characters rejected before any matcher
/// runs, first matching template wins, and the linear-time guarantee's defense in depth — a
/// <see cref="System.Text.RegularExpressions.RegexMatchTimeoutException"/> maps to
/// <see cref="TenantIdentifierSourceResultKind.Invalid"/> with a once-per-process warning that names
/// the template only, never the host. Also pins the builder registration semantics
/// for <c>AddHostSource</c>.
/// </summary>
public sealed class HostTenantIdentifierSourceTests : TestBase
{
    private const string TimeoutEventName = "HEADLESS_TENANT_HOST_TEMPLATE_TIMEOUT";

    [Fact]
    public void should_find_the_identifier_ignoring_the_port_and_preserving_casing()
    {
        var source = _CreateSource("{tenant}.example.com");
        var context = new DefaultHttpContext();
        context.Request.Host = new HostString("ACME.example.com", 8443);

        var result = source.GetIdentifier(context);

        result.Kind.Should().Be(TenantIdentifierSourceResultKind.Found);
        result.Identifier.Should().Be("ACME");
    }

    [Fact]
    public void should_strip_one_trailing_dot_before_matching()
    {
        var source = _CreateSource("{tenant}.example.com");
        var context = new DefaultHttpContext();
        context.Request.Host = new HostString("acme.example.com.");

        var result = source.GetIdentifier(context);

        result.Identifier.Should().Be("acme");
    }

    [Fact]
    public void should_return_none_when_the_request_has_no_host_value()
    {
        var source = _CreateSource("{tenant}.example.com");
        var context = new DefaultHttpContext();

        var result = source.GetIdentifier(context);

        result.Should().Be(TenantIdentifierSourceResult.None);
    }

    [Fact]
    public void should_return_none_for_an_ipv4_host_literal_without_consulting_the_matcher()
    {
        // The bare {tenant} template would otherwise match ANY host, including an IP literal —
        // returning None proves the IP guard runs before the matcher.
        var source = _CreateSource("{tenant}");
        var context = new DefaultHttpContext();
        context.Request.Host = new HostString("10.0.1.5");

        var result = source.GetIdentifier(context);

        result.Should().Be(TenantIdentifierSourceResult.None);
    }

    [Theory]
    [InlineData("[::1]")]
    [InlineData("[::1]:8080")]
    public void should_return_none_for_an_ipv6_host_literal(string ipv6Host)
    {
        var source = _CreateSource("{tenant}");
        var context = new DefaultHttpContext();
        context.Request.Host = new HostString(ipv6Host);

        var result = source.GetIdentifier(context);

        result.Should().Be(TenantIdentifierSourceResult.None);
    }

    [Fact]
    public void should_return_none_for_a_host_matching_no_template()
    {
        var source = _CreateSource("{tenant}.example.com");
        var context = new DefaultHttpContext();
        context.Request.Host = new HostString("example.com");

        source.GetIdentifier(context).Should().Be(TenantIdentifierSourceResult.None);
    }

    [Fact]
    public void should_return_none_for_a_host_over_253_characters_without_consulting_the_matcher()
    {
        var source = _CreateSource("{tenant}");
        var context = new DefaultHttpContext();
        context.Request.Host = new HostString(new string('a', 254));

        source.GetIdentifier(context).Should().Be(TenantIdentifierSourceResult.None);
    }

    [Fact]
    public void should_return_none_for_a_host_with_300_labels_without_consulting_the_matcher()
    {
        var source = _CreateSource("{tenant}");
        var context = new DefaultHttpContext();
        context.Request.Host = new HostString(string.Join('.', Enumerable.Repeat("a", 300)));

        source.GetIdentifier(context).Should().Be(TenantIdentifierSourceResult.None);
    }

    [Fact]
    public void should_return_the_first_matching_template()
    {
        var source = _CreateSource("{tenant}.dev", "{tenant}");
        var context = new DefaultHttpContext();
        context.Request.Host = new HostString("acme.dev");

        // The bare {tenant} template would yield the whole host "acme.dev"; the more specific
        // template was registered first, so its capture wins.
        source.GetIdentifier(context).Identifier.Should().Be("acme");
    }

    [Fact]
    public void should_return_the_later_template_when_an_earlier_one_does_not_match()
    {
        var source = _CreateSource("{tenant}.example.com", "{tenant}.dev");
        var context = new DefaultHttpContext();
        context.Request.Host = new HostString("acme.dev");

        source.GetIdentifier(context).Identifier.Should().Be("acme");
    }

    [Fact]
    public void should_return_invalid_and_warn_once_when_a_template_match_times_out()
    {
        HostTenantIdentifierSource.ResetTemplateTimeoutWarningForTesting();
        var logger = new RecordingLogger();
        // Test seams, not production settings: a 1-tick match timeout only fires on an input far
        // beyond the 253-character DNS guard, so the host cap is raised too. Together they force
        // RegexMatchTimeoutException through the real GetIdentifier path — the production path pins
        // RegexPatterns.MatchTimeout and the 253 cap, which makes the timeout unreachable in production —
        // the "defense in depth" catch is what this test exercises.
        var source = new HostTenantIdentifierSource(
            Options.Create(new HostTenantIdentifierSourceOptions { Templates = ["*.{tenant}.example.com"] }),
            logger,
            matchTimeout: TimeSpan.FromTicks(1),
            maxHostLength: int.MaxValue
        );
        // 'b' labels make the host textually distinct from the template so the assertion below
        // can prove the warning carries the template only, never any host fragment.
        var context = new DefaultHttpContext();
        context.Request.Host = new HostString(string.Join('.', Enumerable.Repeat("b", 50_000)) + ".example.com");

        var first = source.GetIdentifier(context);
        var second = source.GetIdentifier(context);

        first.Kind.Should().Be(TenantIdentifierSourceResultKind.Invalid);
        second.Kind.Should().Be(TenantIdentifierSourceResultKind.Invalid);

        logger.Entries.Should().ContainSingle();
        var entry = logger.Entries[0];
        entry.EventName.Should().Be(TimeoutEventName);
        entry.Message.Should().Contain("*.{tenant}.example.com");
        entry.Message.Should().NotContain("b.b"); // never the host
    }

    [Fact]
    public void should_warn_again_after_the_once_guard_is_reset_for_testing()
    {
        HostTenantIdentifierSource.ResetTemplateTimeoutWarningForTesting();
        var logger = new RecordingLogger();
        var source = new HostTenantIdentifierSource(
            Options.Create(new HostTenantIdentifierSourceOptions { Templates = ["*.{tenant}.example.com"] }),
            logger,
            matchTimeout: TimeSpan.FromTicks(1),
            maxHostLength: int.MaxValue
        );
        var context = new DefaultHttpContext();
        context.Request.Host = new HostString(string.Join('.', Enumerable.Repeat("b", 50_000)) + ".example.com");

        _ = source.GetIdentifier(context);
        logger.Entries.Should().ContainSingle();

        HostTenantIdentifierSource.ResetTemplateTimeoutWarningForTesting();
        _ = source.GetIdentifier(context);

        logger.Entries.Should().HaveCount(2);
    }

    [Fact]
    public void should_throw_when_options_carry_an_unparsable_template()
    {
        // Options validation normally prevents this; constructing the source directly with an
        // invalid template must still fail loudly rather than silently skip the template.
        var act = () => _CreateSource("{tenant}.{tenant}.com");

        act.Should().Throw<InvalidOperationException>().WithMessage($"*{TenantIdentifierSourceTemplateText}*");
    }

    // --- registration: descriptor dedupes, options contributions accumulate ---

    [Fact]
    public void should_register_one_host_source_descriptor_holding_every_contributed_template()
    {
        var services = new ServiceCollection();
        var builder = new HeadlessTenantCatalogResolutionBuilder(services);

        builder.AddHostSource("{tenant}.a.com").AddHostSource("{tenant}.b.com");

        var descriptors = services.Where(d => d.ServiceType == typeof(ITenantIdentifierSource)).ToList();

        descriptors.Should().ContainSingle();
        descriptors[0].ImplementationType.Should().Be<HostTenantIdentifierSource>();
    }

    [Fact]
    public void should_accumulate_templates_across_add_host_source_calls_and_resolve_identifiers()
    {
        var services = new ServiceCollection().AddLogging();
        var builder = new HeadlessTenantCatalogResolutionBuilder(services);

        builder.AddHostSource("{tenant}.a.com").AddHostSource("{tenant}.b.com");

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<HostTenantIdentifierSourceOptions>>().Value;

        options.Templates.Should().Equal(["{tenant}.a.com", "{tenant}.b.com"]);

        var source = provider.GetRequiredService<IEnumerable<ITenantIdentifierSource>>().Single();
        var context = new DefaultHttpContext();
        context.Request.Host = new HostString("acme.b.com");

        source.GetIdentifier(context).Identifier.Should().Be("acme");
    }

    [Fact]
    public void should_bind_templates_from_configuration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["Templates:0"] = "{tenant}.a.com",
                    ["Templates:1"] = "{tenant}.b.com",
                }
            )
            .Build();
        var services = new ServiceCollection();
        var builder = new HeadlessTenantCatalogResolutionBuilder(services);

        builder.AddHostSource(configuration);

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<HostTenantIdentifierSourceOptions>>().Value;

        options.Templates.Should().Equal(["{tenant}.a.com", "{tenant}.b.com"]);
    }

    [Fact]
    public void should_apply_the_configure_overload()
    {
        var services = new ServiceCollection();
        var builder = new HeadlessTenantCatalogResolutionBuilder(services);

        builder.AddHostSource(options => options.Templates.Add("{tenant}.a.com"));

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<HostTenantIdentifierSourceOptions>>().Value;

        options.Templates.Should().Equal(["{tenant}.a.com"]);
    }

    [Fact]
    public void should_reject_null_add_host_source_arguments()
    {
        var services = new ServiceCollection();
        var builder = new HeadlessTenantCatalogResolutionBuilder(services);

        var templateAct = () => builder.AddHostSource((string)null!);
        var configureAct = () => builder.AddHostSource((Action<HostTenantIdentifierSourceOptions>)null!);
        var configurationAct = () => builder.AddHostSource((IConfiguration)null!);
        var whitespaceAct = () => builder.AddHostSource("   ");

        templateAct.Should().Throw<ArgumentException>();
        configureAct.Should().Throw<ArgumentNullException>();
        configurationAct.Should().Throw<ArgumentNullException>();
        whitespaceAct.Should().Throw<ArgumentException>();
    }

    private const string TenantIdentifierSourceTemplateText = "{tenant}.{tenant}.com";

    private static HostTenantIdentifierSource _CreateSource(params string[] templates)
    {
        return new HostTenantIdentifierSource(
            Options.Create(new HostTenantIdentifierSourceOptions { Templates = [.. templates] }),
            NullLogger<HostTenantIdentifierSource>.Instance
        );
    }

    private sealed class RecordingLogger : ILogger<HostTenantIdentifierSource>
    {
        public List<(string? EventName, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            Entries.Add((eventId.Name, formatter(state, exception)));
        }
    }
}
