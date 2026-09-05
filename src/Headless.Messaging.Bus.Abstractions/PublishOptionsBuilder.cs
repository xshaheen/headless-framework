// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Messaging;

/// <summary>Authors options for broadcast messages without changing delivery policy.</summary>
/// <remarks>
/// Supports sequential reuse; concurrent mutation is not supported. Headers are copied when supplied and on every
/// <see cref="Build"/>. Building preserves canonical defaults and does not validate or accept a message for delivery.
/// </remarks>
[PublicAPI]
public sealed class PublishOptionsBuilder
{
    private Dictionary<string, string?>? _headers;
    private string? _correlationId;
    private string? _causationId;
    private string? _messageId;
    private string? _tenantId;
    private TimeSpan? _delay;

    /// <summary>Creates an empty builder that preserves the canonical options defaults.</summary>
    public PublishOptionsBuilder() { }

    /// <summary>Sets one ordinal header key; repeated keys replace their value, including null.</summary>
    /// <exception cref="ArgumentNullException">The header name is null.</exception>
    public PublishOptionsBuilder WithHeader(string name, string? value)
    {
        Argument.IsNotNull(name);
        (_headers ??= new(StringComparer.Ordinal))[name] = value;
        return this;
    }

    /// <summary>Eagerly copies and merges headers; an empty collection preserves an explicit empty dictionary.</summary>
    /// <remarks>If enumeration fails, entries already merged remain local to this builder.</remarks>
    /// <exception cref="ArgumentNullException">The collection or a header name is null.</exception>
    public PublishOptionsBuilder WithHeaders(IEnumerable<KeyValuePair<string, string?>> headers)
    {
        Argument.IsNotNull(headers);
        _headers ??= new(StringComparer.Ordinal);
        foreach (var header in headers)
        {
            WithHeader(header.Key, header.Value);
        }

        return this;
    }

    /// <summary>Sets business correlation; null removes the explicit override.</summary>
    public PublishOptionsBuilder WithCorrelationId(string? correlationId)
    {
        _correlationId = correlationId;
        return this;
    }

    /// <summary>Sets the immediate business cause; null removes the explicit override.</summary>
    public PublishOptionsBuilder WithCausationId(string? causationId)
    {
        _causationId = causationId;
        return this;
    }

    /// <summary>Sets message identity; null removes the explicit override.</summary>
    public PublishOptionsBuilder WithMessageId(string? messageId)
    {
        _messageId = messageId;
        return this;
    }

    /// <summary>Sets the tenant override; null leaves tenant resolution to the publisher.</summary>
    public PublishOptionsBuilder WithTenantId(string? tenantId)
    {
        _tenantId = tenantId;
        return this;
    }

    /// <summary>Sets a delivery delay; null removes it. The publisher validates that supplied delays are positive.</summary>
    public PublishOptionsBuilder WithDelay(TimeSpan? delay)
    {
        _delay = delay;
        return this;
    }

    /// <summary>Creates a canonical options snapshot with its own independently mutable headers.</summary>
    public PublishOptions Build() =>
        new()
        {
            Headers = _headers is null ? null : new Dictionary<string, string?>(_headers, StringComparer.Ordinal),
            CorrelationId = _correlationId,
            CausationId = _causationId,
            MessageId = _messageId,
            TenantId = _tenantId,
            Delay = _delay,
        };
}
