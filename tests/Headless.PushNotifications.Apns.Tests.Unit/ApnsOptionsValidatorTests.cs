// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Cryptography;
using Headless.PushNotifications.Apns;
using Headless.Testing.Tests;

namespace Tests;

public sealed class ApnsOptionsValidatorTests : TestBase
{
    private static readonly ApnsOptionsValidator _Validator = new();
    private static readonly string _EcPem = _CreateEcPem();

    [Fact]
    public void should_be_valid_when_required_values_present_with_defaults()
    {
        // when
        var result = _Validator.Validate(_CreateOptions());

        // then
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void should_default_to_production_alert_immediate()
    {
        // when
        var options = _CreateOptions();

        // then
        options.Environment.Should().Be(ApnsEnvironment.Production);
        options.PushType.Should().Be(ApnsPushType.Alert);
        options.Priority.Should().Be(ApnsPriority.Immediate);
        ((int)options.Priority).Should().Be(10);
        options.TreatBadDeviceTokenAsUnregistered.Should().BeFalse();
        options.MaxConcurrency.Should().Be(100);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ABC123DEF")]
    [InlineData("ABC123DEFGH")]
    [InlineData("ABC-23DEFG")]
    public void should_be_invalid_when_key_id_is_missing_or_malformed(string keyId)
    {
        // given
        var options = _CreateOptions();
        options.KeyId = keyId;

        // when
        var result = _Validator.Validate(options);

        // then
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(ApnsOptions.KeyId));
    }

    [Theory]
    [InlineData("")]
    [InlineData("TEAM12345")]
    [InlineData("TEAM1234567")]
    public void should_be_invalid_when_team_id_is_missing_or_malformed(string teamId)
    {
        // given
        var options = _CreateOptions();
        options.TeamId = teamId;

        // when
        var result = _Validator.Validate(options);

        // then
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(ApnsOptions.TeamId));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void should_be_invalid_when_bundle_id_is_blank(string bundleId)
    {
        // given
        var options = _CreateOptions();
        options.BundleId = bundleId;

        // when
        var result = _Validator.Validate(options);

        // then
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(ApnsOptions.BundleId));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(1000, true)]
    [InlineData(1001, false)]
    public void should_validate_max_concurrency_range(int maxConcurrency, bool expectedValid)
    {
        // given
        var options = _CreateOptions();
        options.MaxConcurrency = maxConcurrency;

        // when
        var result = _Validator.Validate(options);

        // then
        result.IsValid.Should().Be(expectedValid);
    }

    [Fact]
    public void should_be_invalid_when_enum_values_are_undefined()
    {
        // given
        var options = _CreateOptions();
        options.Environment = (ApnsEnvironment)42;
        options.PushType = (ApnsPushType)42;
        options.Priority = (ApnsPriority)42;

        // when
        var result = _Validator.Validate(options);

        // then
        result
            .Errors.Select(e => e.PropertyName)
            .Should()
            .Contain([nameof(ApnsOptions.Environment), nameof(ApnsOptions.PushType), nameof(ApnsOptions.Priority)]);
    }

    [Fact]
    public void should_be_invalid_when_private_key_is_rsa()
    {
        // given
        using var rsa = RSA.Create(2048);
        var options = _CreateOptions();
        options.PrivateKey = rsa.ExportPkcs8PrivateKeyPem();

        // when
        var result = _Validator.Validate(options);

        // then
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(ApnsOptions.PrivateKey));
        result.Errors.Should().NotContain(e => e.ErrorMessage.Contains(options.PrivateKey, StringComparison.Ordinal));
    }

    [Fact]
    public void should_be_invalid_when_private_key_is_on_another_curve()
    {
        // given
        using var p384 = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        var options = _CreateOptions();
        options.PrivateKey = p384.ExportPkcs8PrivateKeyPem();

        // when
        var result = _Validator.Validate(options);

        // then
        result.Errors.Should().Contain(e => e.PropertyName == nameof(ApnsOptions.PrivateKey));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a pem at all")]
    [InlineData("-----BEGIN PRIVATE KEY-----\nZ2FyYmFnZQ==\n-----END PRIVATE KEY-----")]
    public void should_be_invalid_without_echoing_a_malformed_private_key(string privateKey)
    {
        // given
        var options = _CreateOptions();
        options.PrivateKey = privateKey;

        // when
        var result = _Validator.Validate(options);

        // then
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(ApnsOptions.PrivateKey));

        if (privateKey.Length > 0)
        {
            result.Errors.Should().NotContain(e => e.ErrorMessage.Contains(privateKey, StringComparison.Ordinal));
        }
    }

    [Fact]
    public void should_redact_private_key_from_to_string()
    {
        // given
        var options = _CreateOptions();

        // when
        var text = options.ToString();

        // then
        text.Should().NotContain(options.PrivateKey);
        text.Should().NotContain("PRIVATE KEY");
        text.Should().Contain("[REDACTED]");
    }

    private static ApnsOptions _CreateOptions()
    {
        return new ApnsOptions
        {
            KeyId = "ABC123DEFG",
            TeamId = "TEAM123456",
            PrivateKey = _EcPem,
            BundleId = "com.example.app",
        };
    }

    private static string _CreateEcPem()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        return key.ExportPkcs8PrivateKeyPem();
    }
}
