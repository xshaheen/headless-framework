// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using System.Security.Cryptography;
using FluentValidation;
using FluentValidation.Results;
using Headless.PushNotifications.Apns.Internal;

namespace Headless.PushNotifications.Apns;

/// <summary>
/// Represents configuration options for the Apple Push Notification service.
/// </summary>
/// <remarks>
/// Supports either token authentication with <see cref="KeyId"/>, <see cref="TeamId"/>, and <see cref="PrivateKey"/>,
/// or certificate authentication with <see cref="Certificate"/> and <see cref="CertificatePassword"/>.
/// </remarks>
[PublicAPI]
public sealed class ApnsOptions
{
    /// <summary>
    /// Gets or sets the 10-character APNs signing key identifier used in token authentication mode.
    /// </summary>
    public string? KeyId { get; set; }

    /// <summary>
    /// Gets or sets the 10-character Apple Developer team identifier used in token authentication mode.
    /// </summary>
    public string? TeamId { get; set; }

    /// <summary>
    /// Gets or sets the PEM-encoded P-256 EC private key text used in token authentication mode.
    /// </summary>
    /// <remarks>
    /// Contains sensitive private key material and is excluded from serialization.
    /// </remarks>
    [JsonIgnore]
    public string? PrivateKey { get; set; }

    /// <summary>
    /// Gets or sets the Base64-encoded PKCS#12 provider certificate data used in certificate authentication mode.
    /// </summary>
    /// <remarks>
    /// Contains sensitive private key material and is excluded from serialization.
    /// </remarks>
    [JsonIgnore]
    public string? Certificate { get; set; }

    /// <summary>Gets or sets the password for <see cref="Certificate"/> in certificate authentication mode.</summary>
    /// <remarks>Contains sensitive material and is excluded from serialization.</remarks>
    [JsonIgnore]
    public string? CertificatePassword { get; set; }

    /// <summary>
    /// Gets or sets the application bundle identifier sent as the <c>apns-topic</c> header.
    /// </summary>
    public required string BundleId { get; set; }

    /// <summary>
    /// Gets or sets the target APNs environment. Defaults to <see cref="ApnsEnvironment.Production"/>.
    /// </summary>
    public ApnsEnvironment Environment { get; set; } = ApnsEnvironment.Production;

    /// <summary>
    /// Gets or sets the default push type sent as the <c>apns-push-type</c> header. Defaults to <see cref="ApnsPushType.Alert"/>.
    /// </summary>
    public ApnsPushType PushType { get; set; } = ApnsPushType.Alert;

    /// <summary>
    /// Gets or sets the default delivery priority sent as the <c>apns-priority</c> header. Defaults to <see cref="ApnsPriority.Immediate"/>.
    /// </summary>
    public ApnsPriority Priority { get; set; } = ApnsPriority.Immediate;

    /// <summary>
    /// Gets or sets a value indicating whether a <c>BadDeviceToken</c> rejection is treated as unregistered. Defaults to <see langword="false"/>.
    /// </summary>
    public bool TreatBadDeviceTokenAsUnregistered { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of concurrent requests in multicast sends. Defaults to 100.
    /// </summary>
    public int MaxConcurrency { get; set; } = 100;

    /// <summary>
    /// Gets or sets a value indicating whether requests connect via port 2197 instead of port 443. Defaults to <see langword="false"/>.
    /// </summary>
    public bool UseAlternativePort { get; set; }

    /// <summary>
    /// Gets or sets the optional web proxy for APNs HTTP connections. Defaults to <see langword="null"/>.
    /// </summary>
    [JsonIgnore]
    public IWebProxy? Proxy { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of concurrent TCP connections opened to APNs. Defaults to 4.
    /// </summary>
    public int MaxConnections { get; set; } = 4;

    /// <summary>Determines whether these options configure certificate authentication mode.</summary>
    internal bool UsesCertificate => !string.IsNullOrWhiteSpace(Certificate);

    /// <inheritdoc />
    public override string ToString()
    {
        return $"ApnsOptions {{ KeyId = {KeyId}, TeamId = {TeamId}, PrivateKey = [REDACTED], Certificate = [REDACTED], CertificatePassword = [REDACTED], BundleId = {BundleId}, Environment = {Environment}, UseAlternativePort = {UseAlternativePort}, Proxy = {(Proxy is null ? "none" : "configured")} }}";
    }
}

/// <summary>
/// Specifies the target APNs server environment.
/// </summary>
[PublicAPI]
public enum ApnsEnvironment
{
    /// <summary>The production environment (<c>api.push.apple.com</c>).</summary>
    Production = 0,

    /// <summary>The development sandbox environment (<c>api.sandbox.push.apple.com</c>).</summary>
    Sandbox = 1,
}

/// <summary>
/// Specifies the notification push type for APNs processing.
/// </summary>
[PublicAPI]
public enum ApnsPushType
{
    /// <summary>A user-visible notification with an alert.</summary>
    Alert = 0,

    /// <summary>A VoIP notification delivered to PushKit.</summary>
    Voip = 1,
}

/// <summary>
/// Specifies the APNs delivery priority mapped to the <c>apns-priority</c> header.
/// </summary>
#pragma warning disable CA1008 // The values are the apns-priority header values, and APNs defines no zero priority.
[PublicAPI]
public enum ApnsPriority
{
    /// <summary>Prioritizes device battery power. Notifications can be delayed.</summary>
    PowerPrioritized = 1,

    /// <summary>Delivers based on device power considerations.</summary>
    PowerConsiderate = 5,

    /// <summary>Delivers immediately.</summary>
    Immediate = 10,
}
#pragma warning restore CA1008

/// <summary>
/// Validates <see cref="ApnsOptions"/> instances.
/// </summary>
internal sealed class ApnsOptionsValidator : AbstractValidator<ApnsOptions>
{
    private const int _AppleIdLength = 10;

    // The named-curve OID for P-256 (secp256r1), the only curve APNs accepts for ES256.
    private const string _P256Oid = "1.2.840.10045.3.1.7";

    private const string _ModeMessage =
        "APNs needs exactly one authentication mode: token (KeyId, TeamId, and PrivateKey) or certificate (Certificate and CertificatePassword).";

    private readonly TimeProvider _timeProvider;

    public ApnsOptionsValidator()
        : this(TimeProvider.System) { }

    public ApnsOptionsValidator(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;

        RuleFor(x => x)
            .Must(x => _UsesToken(x) != _UsesCertificate(x))
            .OverridePropertyName("Authentication")
            .WithMessage(_ModeMessage);

        When(
            x => _UsesToken(x) && !_UsesCertificate(x),
            () =>
            {
                RuleFor(x => x.KeyId)
                    .Must(_IsAppleId)
                    .WithMessage("APNs KeyId must be the 10-character key identifier (letters and digits).");

                RuleFor(x => x.TeamId)
                    .Must(_IsAppleId)
                    .WithMessage("APNs TeamId must be the 10-character team identifier (letters and digits).");

                // Property values are omitted from error messages to avoid leaking sensitive key contents.
                RuleFor(x => x.PrivateKey)
                    .NotEmpty()
                    .WithMessage("APNs PrivateKey must be provided.")
                    .Must(_IsP256PrivateKey)
                    .WithMessage(
                        "APNs PrivateKey must be the PEM text of a P-256 EC private key (the .p8 file content)."
                    );
            }
        );

        // Validates certificate structure and expiration without logging private key or password material.
        RuleFor(x => x.Certificate).Custom(_ValidateCertificate).When(x => _UsesCertificate(x) && !_UsesToken(x));

        RuleFor(x => x.BundleId).NotEmpty().WithMessage("APNs BundleId must be provided.");

        RuleFor(x => x.Environment).IsInEnum().WithMessage("APNs Environment must be Production or Sandbox.");
        RuleFor(x => x.PushType).IsInEnum().WithMessage("APNs PushType must be Alert or Voip.");
        RuleFor(x => x.Priority)
            .IsInEnum()
            .WithMessage("APNs Priority must be Immediate, PowerConsiderate, or PowerPrioritized.");

        RuleFor(x => x.MaxConcurrency)
            .InclusiveBetween(1, 1000)
            .WithMessage("APNs MaxConcurrency must be between 1 and 1000.");

        RuleFor(x => x.MaxConnections)
            .InclusiveBetween(1, 1000)
            .WithMessage("APNs MaxConnections must be between 1 and 1000.");
    }

    private static bool _UsesToken(ApnsOptions options)
    {
        return !string.IsNullOrWhiteSpace(options.KeyId)
            || !string.IsNullOrWhiteSpace(options.TeamId)
            || !string.IsNullOrWhiteSpace(options.PrivateKey);
    }

    private static bool _UsesCertificate(ApnsOptions options)
    {
        return options.UsesCertificate || !string.IsNullOrEmpty(options.CertificatePassword);
    }

    private void _ValidateCertificate(string? certificateText, ValidationContext<ApnsOptions> context)
    {
        var options = context.InstanceToValidate;

        if (string.IsNullOrWhiteSpace(certificateText))
        {
            context.AddFailure(
                new ValidationFailure(
                    nameof(ApnsOptions.Certificate),
                    "APNs Certificate must be provided when CertificatePassword is set."
                )
            );

            return;
        }

        using var certificate = ApnsCertificateLoader.LoadValid(
            certificateText,
            options.CertificatePassword,
            _timeProvider.GetUtcNow(),
            out var errors
        );

        foreach (var error in errors)
        {
            context.AddFailure(new ValidationFailure(nameof(ApnsOptions.Certificate), error));
        }
    }

    private static bool _IsAppleId(string? value)
    {
        return value is { Length: _AppleIdLength } && value.All(char.IsAsciiLetterOrDigit);
    }

    private static bool _IsP256PrivateKey(string? pem)
    {
        if (string.IsNullOrWhiteSpace(pem))
        {
            return true;
        }

        using var key = ECDsa.Create();

        try
        {
            key.ImportFromPem(pem);
            var curve = key.ExportParameters(includePrivateParameters: true).Curve;

            return curve.IsNamed && string.Equals(curve.Oid.Value, _P256Oid, StringComparison.Ordinal);
        }
        catch (Exception e) when (e is ArgumentException or CryptographicException)
        {
            // ImportFromPem throws ArgumentException for non-PEM formats and CryptographicException for unsupported curves or corrupt keys.
            return false;
        }
    }
}
