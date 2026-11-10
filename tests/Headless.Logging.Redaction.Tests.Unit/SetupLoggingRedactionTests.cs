// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Logging;
using Headless.Testing.Tests;
using Microsoft.Extensions.Compliance.Classification;
using Microsoft.Extensions.Compliance.Redaction;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class SetupLoggingRedactionTests : TestBase
{
    // Built from a fixed made-up phrase at run time, so the test needs no key-shaped literal. 33 bytes encode to the
    // 44 Base64 characters HmacRedactorOptions requires.
    private static readonly string _HmacKey = Convert.ToBase64String("test-key-that-is-long-enough-1234"u8);

    [Theory]
    [MemberData(nameof(HeadlessClassifications))]
    public void should_erase_every_headless_classification_when_no_hmac_key_is_given(string name)
    {
        // given
        var provider = _BuildProvider(services => services.AddHeadlessRedaction());

        // when
        var redacted = provider
            .GetRedactor(new DataClassificationSet(_Classification(name)))
            .Redact("alice@example.com");

        // then
        redacted.Should().BeEmpty();
    }

    [Theory]
    [InlineData(nameof(HeadlessDataClassifications.Private))]
    [InlineData(nameof(HeadlessDataClassifications.Identifier))]
    public void should_pseudonymize_private_data_and_identifiers_when_an_hmac_key_is_given(string name)
    {
        // given
        var provider = _BuildProvider(services =>
            services.AddHeadlessRedaction(setup => setup.UseHmac(_ConfigureHmac))
        );
        var redactor = provider.GetRedactor(new DataClassificationSet(_Classification(name)));

        // when
        var first = redactor.Redact("alice@example.com");
        var second = redactor.Redact("alice@example.com");
        var other = redactor.Redact("bob@example.com");

        // then: stable for the same value, distinct across values, and never the value itself
        first.Should().NotBeNullOrEmpty().And.NotContain("alice");
        second.Should().Be(first);
        other.Should().NotBe(first);
    }

    [Fact]
    public void should_still_erase_secrets_when_an_hmac_key_is_given()
    {
        // given
        var provider = _BuildProvider(services =>
            services.AddHeadlessRedaction(setup => setup.UseHmac(_ConfigureHmac))
        );

        // when
        var redacted = provider
            .GetRedactor(new DataClassificationSet(HeadlessDataClassifications.Secret))
            .Redact("hunter2");

        // then
        redacted.Should().BeEmpty();
    }

    [Fact]
    public void should_read_the_hmac_key_from_configuration()
    {
        // given
        var section = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["Redaction:Hmac:Key"] = _HmacKey,
                    ["Redaction:Hmac:KeyId"] = "7",
                }
            )
            .Build()
            .GetSection("Redaction:Hmac");
        var provider = _BuildProvider(services => services.AddHeadlessRedaction(setup => setup.UseHmac(section)));

        // when
        var redacted = provider
            .GetRedactor(new DataClassificationSet(HeadlessDataClassifications.Private))
            .Redact("alice@example.com");

        // then
        redacted.Should().NotBeNullOrEmpty().And.NotContain("alice");
    }

    [Fact]
    public void should_erase_an_unmapped_classification_and_a_combined_set()
    {
        // given
        var provider = _BuildProvider(services =>
            services.AddHeadlessRedaction(setup => setup.UseHmac(_ConfigureHmac))
        );
        var combined = new DataClassificationSet([
            HeadlessDataClassifications.Private,
            HeadlessDataClassifications.Identifier,
        ]);

        // when
        var unmapped = provider.GetRedactor(new DataClassificationSet(new DataClassification("Other", "Thing")));
        var both = provider.GetRedactor(combined);

        // then: neither has a redactor of its own, so both fall back to erasing
        unmapped.Redact("alice").Should().BeEmpty();
        both.Redact("alice").Should().BeEmpty();
    }

    [Fact]
    public void should_let_configure_replace_a_default()
    {
        // given
        var provider = _BuildProvider(services =>
            services.AddHeadlessRedaction(setup =>
                setup.ConfigureRedaction(redaction =>
                    redaction.SetRedactor<PrefixMaskRedactor>(
                        new DataClassificationSet(HeadlessDataClassifications.Identifier)
                    )
                )
            )
        );

        // when
        var redacted = provider
            .GetRedactor(new DataClassificationSet(HeadlessDataClassifications.Identifier))
            .Redact("device-token-123456");

        // then
        redacted.Should().Be("device-t***");
    }

    [Fact]
    public void should_throw_when_arguments_are_null()
    {
        // given
        var services = new ServiceCollection();

        // when
        var nullSection = () => services.AddHeadlessRedaction(setup => setup.UseHmac(hmacSection: null!));
        var nullHmac = () => services.AddHeadlessRedaction(setup => setup.UseHmac(configureHmac: null!));
        var emptyPattern = () => services.AddHeadlessRedaction(setup => setup.MaskText(pattern: ""));

        // then
        nullSection.Should().Throw<ArgumentNullException>();
        nullHmac.Should().Throw<ArgumentNullException>();
        emptyPattern.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void should_keep_the_app_hmac_key_when_a_library_also_registers_redaction(bool libraryFirst)
    {
        // given: a library's bare call before or after the app's keyed call
        var provider = _BuildProvider(services =>
        {
            if (libraryFirst)
            {
                services.AddHeadlessRedaction();
            }

            services.AddHeadlessRedaction(setup => setup.UseHmac(_ConfigureHmac));

            if (!libraryFirst)
            {
                services.AddHeadlessRedaction();
            }
        });

        // when
        var privateData = provider
            .GetRedactor(new DataClassificationSet(HeadlessDataClassifications.Private))
            .Redact("alice@example.com");
        var secret = provider
            .GetRedactor(new DataClassificationSet(HeadlessDataClassifications.Secret))
            .Redact("hunter2");

        // then: the pseudonym survives in both orders, and secrets stay erased
        privateData.Should().NotBeNullOrEmpty().And.NotContain("alice");
        secret.Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(HeadlessClassifications))]
    public void should_carry_the_matching_classification_on_each_attribute(string name)
    {
        // given
        DataClassificationAttribute attribute = name switch
        {
            nameof(HeadlessDataClassifications.Secret) => new SecretDataAttribute(),
            nameof(HeadlessDataClassifications.Private) => new PrivateDataAttribute(),
            _ => new IdentifierDataAttribute(),
        };

        // then
        attribute.Classification.Should().Be(_Classification(name));
        attribute.Classification.TaxonomyName.Should().Be(HeadlessDataClassifications.TaxonomyName);
    }

    public static TheoryData<string> HeadlessClassifications =>
        [
            nameof(HeadlessDataClassifications.Secret),
            nameof(HeadlessDataClassifications.Private),
            nameof(HeadlessDataClassifications.Identifier),
        ];

    private static DataClassification _Classification(string name) =>
        name switch
        {
            nameof(HeadlessDataClassifications.Secret) => HeadlessDataClassifications.Secret,
            nameof(HeadlessDataClassifications.Private) => HeadlessDataClassifications.Private,
            _ => HeadlessDataClassifications.Identifier,
        };

    private static void _ConfigureHmac(HmacRedactorOptions options)
    {
        options.Key = _HmacKey;
        options.KeyId = 7;
    }

    private static IRedactorProvider _BuildProvider(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();
        configure(services);

        return services.BuildServiceProvider().GetRequiredService<IRedactorProvider>();
    }
}
