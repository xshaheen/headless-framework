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
    /// Must be at least 100 milliseconds. Defaults to the Pulsar.Client default of one minute.
    /// </summary>
    public TimeSpan NegativeAckRedeliveryDelay { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// TLS configuration for the Pulsar connection. When <see langword="null"/>, TLS is disabled
    /// and the client connects over plain-text.
    /// </summary>
    public PulsarTlsOptions? TlsOptions { get; set; }
}

internal sealed class PulsarMessagingOptionsValidator : AbstractValidator<PulsarMessagingOptions>
{
    public PulsarMessagingOptionsValidator()
    {
        RuleFor(x => x.ServiceUrl).NotEmpty();
        RuleFor(x => x.NegativeAckRedeliveryDelay).GreaterThanOrEqualTo(TimeSpan.FromMilliseconds(100));
    }
}
