// Copyright (c) Mahmoud Shaheen. All rights reserved.

// ReSharper disable once CheckNamespace

using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Pulsar.Client.Api;

namespace Framework.Messages;

/// <summary>
/// Provides programmatic configuration for the CAP pulsar project.
/// </summary>
public class PulsarOptions
{
    public required string ServiceUrl { get; set; }

    public bool EnableClientLog { get; set; }

    public PulsarTlsOptions? TlsOptions { get; set; }
}

public class PulsarTlsOptions
{
    private static readonly PulsarClientConfiguration _Default = PulsarClientConfiguration.Default;

    public bool UseTls { get; set; } = _Default.UseTls;
    public bool TlsHostnameVerificationEnable { get; set; } = _Default.TlsHostnameVerificationEnable;
    public bool TlsAllowInsecureConnection { get; set; } = _Default.TlsAllowInsecureConnection;
    public X509Certificate2 TlsTrustCertificate { get; set; } = _Default.TlsTrustCertificate;
    public Authentication Authentication { get; set; } = _Default.Authentication;
    public SslProtocols TlsProtocols { get; set; } = _Default.TlsProtocols;
}
