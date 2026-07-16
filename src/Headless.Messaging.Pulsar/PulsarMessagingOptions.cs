// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using FluentValidation;
using Pulsar.Client.Api;

namespace Headless.Messaging.Pulsar;

/// <summary>
/// Configuration options for the Apache Pulsar messaging transport.
/// </summary>
/// <remarks>
/// TLS is configured via <see cref="TlsOptions"/>; when <see langword="null"/>, plain-text
/// connections are used. The service URL scheme must match the chosen security mode
/// (<c>pulsar://</c> for plain-text, <c>pulsar+ssl://</c> for TLS).
/// </remarks>
public sealed class PulsarMessagingOptions
{
    /// <summary>
    /// The Pulsar service URL to connect to (for example <c>"pulsar://localhost:6650"</c> or
    /// <c>"pulsar+ssl://broker:6651"</c>).
    /// </summary>
    public required string ServiceUrl { get; set; }

    /// <summary>
    /// When <see langword="true"/>, enables verbose Pulsar client logging through the underlying
    /// client library. Useful for diagnosing connection and protocol issues. Defaults to <see langword="false"/>.
    /// </summary>
    public bool EnableClientLog { get; set; }

    /// <summary>
    /// Delay before a negatively acknowledged message becomes eligible for redelivery.
    /// Defaults to the Pulsar.Client default of one minute.
    /// </summary>
    public TimeSpan NegativeAckRedeliveryDelay { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// TLS configuration for the Pulsar connection. When <see langword="null"/>, TLS is disabled
    /// and the client connects over plain-text.
    /// </summary>
    public PulsarTlsOptions? TlsOptions { get; set; }
}

/// <summary>TLS settings applied to the Pulsar client connection.</summary>
public sealed class PulsarTlsOptions
{
    private static readonly PulsarClientConfiguration _Default = PulsarClientConfiguration.Default;

    /// <summary>
    /// When <see langword="true"/>, the client verifies that the broker's TLS certificate hostname
    /// matches the service URL. Defaults to the Pulsar client library default.
    /// </summary>
    public bool TlsHostnameVerificationEnable { get; set; } = _Default.TlsHostnameVerificationEnable;

    /// <summary>
    /// When <see langword="true"/>, the client accepts broker TLS certificates that cannot be
    /// verified (self-signed). Enable only in development or testing environments.
    /// Defaults to the Pulsar client library default.
    /// </summary>
    public bool TlsAllowInsecureConnection { get; set; } = _Default.TlsAllowInsecureConnection;

    /// <summary>
    /// The X.509 certificate used to verify the broker's TLS certificate chain.
    /// Defaults to the Pulsar client library default.
    /// </summary>
    public X509Certificate2 TlsTrustCertificate { get; set; } = _Default.TlsTrustCertificate;

    /// <summary>
    /// The Pulsar authentication provider (for example mTLS or token authentication).
    /// Defaults to the Pulsar client library default (no authentication).
    /// </summary>
    public Authentication Authentication { get; set; } = _Default.Authentication;

    /// <summary>
    /// The TLS protocol versions allowed for the connection.
    /// Defaults to the Pulsar client library default.
    /// </summary>
    public SslProtocols TlsProtocols { get; set; } = _Default.TlsProtocols;
}

internal sealed class PulsarMessagingOptionsValidator : AbstractValidator<PulsarMessagingOptions>
{
    public PulsarMessagingOptionsValidator()
    {
        RuleFor(x => x.ServiceUrl).NotEmpty();
    }
}
