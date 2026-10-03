// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using FluentValidation;
using Pulsar.Client.Api;

namespace Headless.Messaging.Pulsar;

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
