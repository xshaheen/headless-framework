// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Cryptography;
using FluentValidation;

namespace Headless.PushNotifications.Apns;

/// <summary>
/// Apple Push Notification service (APNs) configuration options for token-based (<c>.p8</c> key) authentication.
/// </summary>
[PublicAPI]
public sealed class ApnsOptions
{
    /// <summary>
    /// The 10-character identifier of the APNs signing key, shown next to the key in the Apple Developer account.
    /// </summary>
    public required string KeyId { get; set; }

    /// <summary>
    /// The 10-character Apple Developer team identifier that owns the signing key.
    /// </summary>
    public required string TeamId { get; set; }

    /// <summary>
    /// The PEM text of the APNs signing key (the content of the <c>AuthKey_*.p8</c> file), a P-256 EC private key.
    /// </summary>
    /// <remarks>
    /// Contains sensitive private key data. Do not log or serialize. Option sets that share
    /// <see cref="TeamId"/> and <see cref="KeyId"/> must carry the same key text, because they share one cached
    /// provider token.
    /// </remarks>
    [JsonIgnore]
    public required string PrivateKey { get; set; }

    /// <summary>
    /// The app's bundle identifier, sent as the <c>apns-topic</c> header. VoIP pushes append <c>.voip</c> to it.
    /// </summary>
    public required string BundleId { get; set; }

    /// <summary>
    /// The APNs environment to deliver to. Default: <see cref="ApnsEnvironment.Production"/>.
    /// </summary>
    /// <remarks>
    /// A device token belongs to the environment the app was built for. Sending it to the other environment is
    /// rejected with <c>BadDeviceToken</c>.
    /// </remarks>
    public ApnsEnvironment Environment { get; set; } = ApnsEnvironment.Production;

    /// <summary>
    /// The push type sent as the <c>apns-push-type</c> header. Default: <see cref="ApnsPushType.Alert"/>.
    /// </summary>
    public ApnsPushType PushType { get; set; } = ApnsPushType.Alert;

    /// <summary>
    /// The delivery priority sent as the <c>apns-priority</c> header. Default: <see cref="ApnsPriority.Immediate"/>.
    /// </summary>
    public ApnsPriority Priority { get; set; } = ApnsPriority.Immediate;

    /// <summary>
    /// Whether a <c>BadDeviceToken</c> rejection reports the token as unregistered. Default: <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// Apple returns <c>BadDeviceToken</c> both for a malformed token and for a token sent to the wrong
    /// environment, so treating it as unregistered can make a misconfigured host discard valid tokens. HTTP 410
    /// always reports the token as unregistered, whatever this setting.
    /// </remarks>
    public bool TreatBadDeviceTokenAsUnregistered { get; set; }

    /// <summary>
    /// The maximum number of requests a multicast send has in flight at once. Default: 100. Valid range: 1-1000.
    /// </summary>
    public int MaxConcurrency { get; set; } = 100;

    /// <inheritdoc />
    public override string ToString()
    {
        return $"ApnsOptions {{ KeyId = {KeyId}, TeamId = {TeamId}, PrivateKey = [REDACTED], BundleId = {BundleId}, Environment = {Environment} }}";
    }
}

/// <summary>
/// The APNs environment a provider delivers to.
/// </summary>
[PublicAPI]
public enum ApnsEnvironment
{
    /// <summary>The production environment, <c>api.push.apple.com</c>, for App Store, TestFlight, and ad hoc builds.</summary>
    Production = 0,

    /// <summary>The development environment, <c>api.sandbox.push.apple.com</c>, for builds signed with a development profile.</summary>
    Sandbox = 1,
}

/// <summary>
/// The APNs push type, which decides how the device handles the notification.
/// </summary>
[PublicAPI]
public enum ApnsPushType
{
    /// <summary>A user-visible notification with an alert.</summary>
    Alert = 0,

    /// <summary>A VoIP notification delivered to PushKit. It raises the payload limit to 5120 bytes.</summary>
    Voip = 1,
}

/// <summary>
/// The APNs delivery priority. The numeric values are the <c>apns-priority</c> header values.
/// </summary>
[PublicAPI]
public enum ApnsPriority
{
    /// <summary>Deliver immediately.</summary>
    Immediate = 10,

    /// <summary>Deliver based on the device's power considerations.</summary>
    PowerConsiderate = 5,

    /// <summary>Prioritize the device's power over all other factors; the notification may be delayed.</summary>
    PowerPrioritized = 1,
}

/// <summary>
/// FluentValidation validator for <see cref="ApnsOptions"/>. Wired up and executed at startup by the
/// <c>UseApns</c> setup methods.
/// </summary>
internal sealed class ApnsOptionsValidator : AbstractValidator<ApnsOptions>
{
    private const int _AppleIdLength = 10;

    // The named-curve OID for P-256 (secp256r1), the only curve APNs accepts for ES256.
    private const string _P256Oid = "1.2.840.10045.3.1.7";

    public ApnsOptionsValidator()
    {
        RuleFor(x => x.KeyId)
            .Must(_IsAppleId)
            .WithMessage("APNs KeyId must be the 10-character key identifier (letters and digits).");

        RuleFor(x => x.TeamId)
            .Must(_IsAppleId)
            .WithMessage("APNs TeamId must be the 10-character team identifier (letters and digits).");

        // The messages never use {PropertyValue}: the key text would otherwise reach OptionsValidationException.
        RuleFor(x => x.PrivateKey)
            .NotEmpty()
            .WithMessage("APNs PrivateKey must be provided.")
            .Must(_IsP256PrivateKey)
            .WithMessage("APNs PrivateKey must be the PEM text of a P-256 EC private key (the .p8 file content).");

        RuleFor(x => x.BundleId).NotEmpty().WithMessage("APNs BundleId must be provided.");

        RuleFor(x => x.Environment).IsInEnum().WithMessage("APNs Environment must be Production or Sandbox.");
        RuleFor(x => x.PushType).IsInEnum().WithMessage("APNs PushType must be Alert or Voip.");
        RuleFor(x => x.Priority)
            .IsInEnum()
            .WithMessage("APNs Priority must be Immediate, PowerConsiderate, or PowerPrioritized.");

        RuleFor(x => x.MaxConcurrency)
            .InclusiveBetween(1, 1000)
            .WithMessage("APNs MaxConcurrency must be between 1 and 1000.");
    }

    private static bool _IsAppleId(string? value)
    {
        return value is { Length: _AppleIdLength } && value.All(char.IsAsciiLetterOrDigit);
    }

    private static bool _IsP256PrivateKey(string? pem)
    {
        if (string.IsNullOrWhiteSpace(pem))
        {
            // NotEmpty already reports this case.
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
            // ImportFromPem reports non-PEM text as ArgumentException and a non-EC or corrupt key as
            // CryptographicException; both mean the configured key is unusable.
            return false;
        }
    }
}
