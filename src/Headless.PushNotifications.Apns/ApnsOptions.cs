// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using System.Security.Cryptography;
using FluentValidation;
using FluentValidation.Results;
using Headless.PushNotifications.Apns.Internals;

namespace Headless.PushNotifications.Apns;

/// <summary>
/// Apple Push Notification service (APNs) configuration options.
/// </summary>
/// <remarks>
/// An instance authenticates in exactly one of two modes: token mode, with <see cref="KeyId"/>, <see cref="TeamId"/>,
/// and <see cref="PrivateKey"/> (a <c>.p8</c> signing key), or certificate mode, with <see cref="Certificate"/> and
/// <see cref="CertificatePassword"/> (a <c>.p12</c> provider certificate). Configuring neither or both fails startup
/// validation.
/// </remarks>
[PublicAPI]
public sealed class ApnsOptions
{
    /// <summary>
    /// Token mode: the 10-character identifier of the APNs signing key, shown next to the key in the Apple Developer
    /// account.
    /// </summary>
    public string? KeyId { get; set; }

    /// <summary>
    /// Token mode: the 10-character Apple Developer team identifier that owns the signing key.
    /// </summary>
    public string? TeamId { get; set; }

    /// <summary>
    /// Token mode: the PEM text of the APNs signing key (the content of the <c>AuthKey_*.p8</c> file), a P-256 EC
    /// private key.
    /// </summary>
    /// <remarks>
    /// Contains sensitive private key data. Do not log or serialize. Option sets that share
    /// <see cref="TeamId"/> and <see cref="KeyId"/> must carry the same key text, because they share one cached
    /// provider token.
    /// </remarks>
    [JsonIgnore]
    public string? PrivateKey { get; set; }

    /// <summary>
    /// Certificate mode: the base64 text of the APNs provider certificate exported as a PKCS#12 (<c>.p12</c>) file,
    /// including its private key.
    /// </summary>
    /// <remarks>
    /// Contains sensitive private key data. Do not log or serialize. The certificate is presented during the TLS
    /// handshake instead of a provider token, and it cannot send <c>location</c>, <c>fileprovider</c>,
    /// <c>liveactivity</c>, <c>widgets</c>, or <c>controls</c> pushes. Apple certificates last one year; the host
    /// fails to start once the certificate has expired, and logs a warning at startup and in a daily check when it
    /// expires within 30 days. When bound configuration reloads with a renewed certificate, new connections present
    /// it without a restart.
    /// </remarks>
    [JsonIgnore]
    public string? Certificate { get; set; }

    /// <summary>Certificate mode: the password that opens <see cref="Certificate"/>, if it has one.</summary>
    /// <remarks>Sensitive. Do not log or serialize.</remarks>
    [JsonIgnore]
    public string? CertificatePassword { get; set; }

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

    /// <summary>
    /// Whether to deliver through port 2197 instead of 443. Default: <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Apple offers port 443 and port 2197 for both <c>api.push.apple.com</c> and
    /// <c>api.sandbox.push.apple.com</c>; some networks that block 443 to non-web endpoints still allow 2197.
    /// The flag applies to both environments and is part of the endpoint the HTTP client is built with.
    /// </para>
    /// <para>
    /// Deliberately not a port number property: APNs accepts exactly two ports, so a boolean cannot express an
    /// invalid endpoint.
    /// </para>
    /// </remarks>
    public bool UseAlternativePort { get; set; }

    /// <summary>
    /// An optional proxy the underlying HTTP handler routes APNs requests through, such as a corporate egress
    /// proxy. Default: <see langword="null"/>, which connects directly.
    /// </summary>
    /// <remarks>
    /// Applied to the primary handler, so it covers every connection the pool opens, including HTTP/2 and TLS
    /// ones. Not serializable: configuration cannot express a live <see cref="IWebProxy"/> instance, so set it from
    /// code through <c>UseApns(options => …)</c>. Use <see cref="WebProxy"/> for an HTTP proxy; a SOCKS proxy needs
    /// a SOCKS-capable <see cref="IWebProxy"/> implementation.
    /// </remarks>
    [JsonIgnore]
    public IWebProxy? Proxy { get; set; }

    /// <summary>
    /// The maximum number of simultaneous TCP connections one instance opens to APNs. Default: 4. Valid range:
    /// 1-1000.
    /// </summary>
    /// <remarks>
    /// <para>
    /// APNs starts each token-authenticated connection with a single stream until it has seen a valid provider
    /// token, and a cold multicast under the default <see cref="MaxConcurrency"/> of 100 against such a server
    /// opens a double-digit number of connections — measured between 7 and 30 across runs on this repository's
    /// one-stream test double — because the runtime injects a new connection for every request still waiting once
    /// the open ones advertise their single-stream limit. Unbounded growth spends file descriptors and TLS
    /// handshakes on requests that a few warm connections would carry once APNs raises the stream limit.
    /// </para>
    /// <para>
    /// The default of 4 follows the sizing advice of the mature APNs clients: pushy (Java) recommends "one or two
    /// connections per thread, not to exceed more than two connections per server", and APNs serves each
    /// environment from several servers behind one host name. Raise it when you saturate CPU or bandwidth before
    /// connection capacity, and lower it to shrink the process's footprint; the bound trades peak cold-start
    /// throughput for a predictable connection count.
    /// </para>
    /// <para>
    /// Implemented with a <see cref="SocketsHttpHandler.ConnectCallback"/> permit, so a request waits for a free
    /// permit — an idle connection's free stream or a closed connection's slot — instead of dialing. The runtime's
    /// own <c>MaxConnectionsPerServer</c> cannot provide this bound: it is enforced only for HTTP/1.1, while APNs
    /// speaks HTTP/2.
    /// </para>
    /// </remarks>
    public int MaxConnections { get; set; } = 4;

    /// <summary>Whether these options select certificate mode rather than token mode.</summary>
    internal bool UsesCertificate => !string.IsNullOrWhiteSpace(Certificate);

    /// <inheritdoc />
    public override string ToString()
    {
        return $"ApnsOptions {{ KeyId = {KeyId}, TeamId = {TeamId}, PrivateKey = [REDACTED], Certificate = [REDACTED], CertificatePassword = [REDACTED], BundleId = {BundleId}, Environment = {Environment}, UseAlternativePort = {UseAlternativePort}, Proxy = {(Proxy is null ? "none" : "configured")} }}";
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

                // The messages never use {PropertyValue}: the key text would otherwise reach
                // OptionsValidationException.
                RuleFor(x => x.PrivateKey)
                    .NotEmpty()
                    .WithMessage("APNs PrivateKey must be provided.")
                    .Must(_IsP256PrivateKey)
                    .WithMessage(
                        "APNs PrivateKey must be the PEM text of a P-256 EC private key (the .p8 file content)."
                    );
            }
        );

        // One custom rule loads the certificate once for every check. Failures carry no attempted value, so neither
        // the certificate nor the password can reach OptionsValidationException.
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
