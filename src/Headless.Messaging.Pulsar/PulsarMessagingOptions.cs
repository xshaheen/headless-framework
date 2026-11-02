// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers;
using System.Security.Cryptography.X509Certificates;
using DotPulsar;
using DotPulsar.Abstractions;
using FluentValidation;

namespace Headless.Messaging.Pulsar;

/// <summary>
/// Configuration options for the Apache Pulsar messaging transport.
/// </summary>
/// <remarks>
/// The service URL scheme selects the security mode: <c>pulsar://</c> connects in plain text and <c>pulsar+ssl://</c>
/// over TLS. <see cref="TlsOptions"/> tunes how the broker certificate is checked and which client certificate is
/// presented.
/// </remarks>
public sealed class PulsarMessagingOptions
{
    /// <summary>
    /// The Pulsar service URL to connect to (for example <c>"pulsar://localhost:6650"</c> or
    /// <c>"pulsar+ssl://broker:6651"</c>).
    /// </summary>
    public required string ServiceUrl { get; set; }

    /// <summary>
    /// When <see langword="true"/>, logs every exception the Pulsar client handles (with the retry or fault decision
    /// the client then takes) and every connection state change of the transport's producers, consumers, and readers.
    /// Useful for diagnosing connection and protocol issues. Defaults to <see langword="false"/>.
    /// </summary>
    public bool EnableClientLog { get; set; }

    /// <summary>
    /// The authentication the client presents to the broker, for example
    /// <c>AuthenticationFactory.Token(...)</c> from DotPulsar. A token factory overload covers OAuth 2.0 and other
    /// short-lived tokens. <see langword="null"/> (default) connects without authentication. For mutual TLS, set
    /// <see cref="PulsarTlsOptions.ClientCertificate"/> instead.
    /// </summary>
    public IAuthentication? Authentication { get; set; }

    /// <summary>
    /// Delay before a negatively acknowledged message is redelivered. Must be at least 100 milliseconds. Defaults to one
    /// minute.
    /// </summary>
    /// <remarks>
    /// The consumer holds each rejected message for this long, then asks the broker to redeliver it, as the Apache
    /// Pulsar clients do; the broker counts each redelivery toward a consumer's dead-letter limit.
    /// </remarks>
    public TimeSpan NegativeAckRedeliveryDelay { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Optional callback that adds headers to an inbound message from native Pulsar metadata, such as the message key,
    /// event time, or a foreign producer's own property names. Each returned header overwrites a message property of the
    /// same name, and runs before the envelope is checked, so it can supply a message id or name a foreign producer
    /// wrote under another property. It cannot choose <c>headless-transport-address</c>, which the transport always
    /// stamps from the topic. A callback that throws is treated as a retryable failure, since a builder that looks up or
    /// translates headers can fail on a brief outage of its own dependency: the failure is logged and the message is
    /// negatively acknowledged, so the broker redelivers it after <see cref="NegativeAckRedeliveryDelay"/>. A message
    /// whose builder keeps failing is redelivered without end unless the consumer sets a dead-letter policy through
    /// <c>UsePulsar(p =&gt; p.DeadLetter(...))</c>, which moves it to the dead-letter topic once that redelivery budget
    /// is spent. An envelope the transport's own check finds malformed is still acknowledged and not redelivered.
    /// </summary>
    public Func<
        IMessage<ReadOnlySequence<byte>>,
        IServiceProvider,
        List<KeyValuePair<string, string>>
    >? CustomHeadersBuilder { get; set; }

    /// <summary>
    /// How the broker's TLS certificate is checked and which client certificate is presented on a
    /// <c>pulsar+ssl://</c> connection. <see langword="null"/> (default) keeps the client defaults: the certificate
    /// authority is verified, the host name is not, and no client certificate is sent.
    /// </summary>
    public PulsarTlsOptions? TlsOptions { get; set; }

    /// <summary>Settings applied to every producer the transport creates.</summary>
    public PulsarProducerOptions Producer { get; set; } = new();
}

/// <summary>Settings applied to every producer the Pulsar transport creates.</summary>
public sealed class PulsarProducerOptions
{
    /// <summary>
    /// The codec that compresses message payloads before they are sent. Consumers decompress transparently; the
    /// transport ships every codec Pulsar defines. Defaults to <see cref="CompressionType.None"/>.
    /// </summary>
    public CompressionType CompressionType { get; set; } = CompressionType.None;

    /// <summary>
    /// How long a send may wait for the broker's acknowledgement before it fails; <see cref="TimeSpan.Zero"/> waits
    /// indefinitely. Defaults to 30 seconds. The client keeps a send pending through a lost connection and retries it
    /// once the connection is back, so a send that times out reports the broker unreachable.
    /// </summary>
    public TimeSpan SendTimeout { get; set; } = TimeSpan.FromSeconds(30);
}

/// <summary>TLS settings applied to the Pulsar client connection.</summary>
public sealed class PulsarTlsOptions
{
    /// <summary>
    /// When <see langword="true"/> (default), the broker's certificate must chain to a trusted authority: the system
    /// store or <see cref="TrustedCertificateAuthority"/>. Disable only in development or testing environments.
    /// </summary>
    public bool VerifyCertificateAuthority { get; set; } = true;

    /// <summary>
    /// When <see langword="true"/>, the broker's certificate must name the host of the service URL. Defaults to
    /// <see langword="false"/>.
    /// </summary>
    public bool VerifyCertificateName { get; set; }

    /// <summary>
    /// When <see langword="true"/> (default), the client checks the broker certificate's revocation status.
    /// </summary>
    public bool CheckCertificateRevocation { get; set; } = true;

    /// <summary>
    /// An additional certificate authority the broker's certificate may chain to, such as a private CA.
    /// <see langword="null"/> (default) trusts only the system store.
    /// </summary>
    public X509Certificate2? TrustedCertificateAuthority { get; set; }

    /// <summary>
    /// The certificate, with its private key, the client presents for mutual TLS authentication.
    /// <see langword="null"/> (default) presents none.
    /// </summary>
    public X509Certificate2? ClientCertificate { get; set; }
}

internal sealed class PulsarMessagingOptionsValidator : AbstractValidator<PulsarMessagingOptions>
{
    public PulsarMessagingOptionsValidator()
    {
        RuleFor(x => x.ServiceUrl)
            .NotEmpty()
            .Must(_IsPulsarUri)
            .WithMessage("'{PropertyName}' must be an absolute pulsar:// or pulsar+ssl:// URL.");
        RuleFor(x => x.NegativeAckRedeliveryDelay).GreaterThanOrEqualTo(TimeSpan.FromMilliseconds(100));
        RuleFor(x => x.Producer).NotNull();
        RuleFor(x => x.Producer.CompressionType).IsInEnum().When(x => x.Producer is not null);
        RuleFor(x => x.Producer.SendTimeout).GreaterThanOrEqualTo(TimeSpan.Zero).When(x => x.Producer is not null);
    }

    private static bool _IsPulsarUri(string? serviceUrl) =>
        Uri.TryCreate(serviceUrl, UriKind.Absolute, out var uri)
        && uri.Host.Length > 0
        && (
            string.Equals(uri.Scheme, "pulsar", StringComparison.OrdinalIgnoreCase)
            || string.Equals(uri.Scheme, "pulsar+ssl", StringComparison.OrdinalIgnoreCase)
        );
}
