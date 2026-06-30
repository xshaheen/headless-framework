// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.CompilerServices;
using Headless.Abstractions;
using Headless.Checks;
using Headless.Messaging.Configuration;
using Headless.Messaging.Messages;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.Internal;

internal interface IMessagePublishRequestFactory
{
    PreparedPublishMessage Create<T>(
        T? contentObj,
        MessageOptions? options = null,
        TimeSpan? delayTime = null,
        IntentType intentType = IntentType.Bus
    );
}

internal sealed class MessagePublishRequestFactory(
    IGuidGenerator idGenerator,
    TimeProvider timeProvider,
    IOptions<MessagingOptions> optionsAccessor,
    IConsumerRegistry consumerRegistry,
    ICurrentTenant currentTenant,
    IMessageMetadataRegistry? metadataRegistry = null,
    IConsumeContextAccessor? consumeContextAccessor = null
) : IMessagePublishRequestFactory
{
    private static readonly HashSet<string> _ReservedHeaders = new(StringComparer.Ordinal)
    {
        Headers.MessageId,
        Headers.CorrelationId,
        Headers.CorrelationSequence,
        Headers.CallbackName,
        Headers.MessageName,
        Headers.Type,
        Headers.SentTime,
        Headers.DelayTime,
        Headers.Intent,
    };

    private static readonly HashSet<string> _ProviderReservedHeaders = new(_ReservedHeaders, StringComparer.Ordinal)
    {
        Headers.TenantId,
        Headers.TraceParent,
    };

    private readonly ConditionalWeakTable<Type, string> _messageNameCache = [];
    private readonly MessagingOptions _options = optionsAccessor.Value;

    public PreparedPublishMessage Create<T>(
        T? contentObj,
        MessageOptions? options = null,
        TimeSpan? delayTime = null,
        IntentType intentType = IntentType.Bus
    )
    {
        if (delayTime is { } requestedDelay)
        {
            Argument.IsPositive(requestedDelay);
        }

        var publishType = typeof(T);
        var explicitMessageType = options?.MessageType;
        var metadataLookupType = contentObj?.GetType() ?? explicitMessageType ?? publishType;
        MessageMetadata? metadata = null;
        metadataRegistry?.TryGet(metadataLookupType, out metadata);

        var messageType = explicitMessageType ?? metadata?.MessageType ?? publishType;
        var messageName = _ResolveMessageName(messageType, options?.MessageName);
        var headers = _CreateHeaders(
            messageType,
            messageName,
            options,
            delayTime,
            _ResolveCorrelationFromSelector(metadata, contentObj, metadataLookupType),
            consumeContextAccessor?.Current?.CorrelationId
        );
        var publishAt = _ResolvePublishAt(delayTime);

        _ApplyProviderHeaderContributions(headers, metadata, contentObj, metadataLookupType);

        headers[Headers.SentTime] = publishAt.UtcDateTime.ToString(CultureInfo.InvariantCulture);
        headers[Headers.Intent] = intentType.ToString();

        if (delayTime.HasValue)
        {
            headers[Headers.DelayTime] = delayTime.Value.ToString();
        }

        _ValidateHeaderValues(headers);

        return new PreparedPublishMessage
        {
            MessageName = messageName,
            PublishAt = publishAt.UtcDateTime,
            Message = new Message(headers, contentObj),
            IntentType = intentType,
        };
    }

    private Dictionary<string, string?> _CreateHeaders(
        Type messageType,
        string messageName,
        MessageOptions? options,
        TimeSpan? delayTime,
        string? selectorCorrelationId,
        string? ambientCorrelationId
    )
    {
        var headers =
            options?.Headers != null
                ? new Dictionary<string, string?>(options.Headers, StringComparer.Ordinal)
                : new Dictionary<string, string?>(StringComparer.Ordinal);

        _ValidateCustomHeaderNames(headers);
        _ApplyTenantId(headers, options);

        var messageId = string.IsNullOrWhiteSpace(options?.MessageId)
            ? idGenerator.Create().ToString("D")
            : _ValidateMessageId(options.MessageId);

        headers[Headers.MessageId] = messageId;
        var correlationId = _ResolveCorrelationId(
            options?.CorrelationId,
            selectorCorrelationId,
            ambientCorrelationId,
            messageId
        );

        headers[Headers.CorrelationId] = correlationId;
        headers[Headers.CorrelationSequence] = (options?.CorrelationSequence ?? 0).ToString(
            CultureInfo.InvariantCulture
        );
        headers[Headers.MessageName] = messageName;
        headers[Headers.Type] = messageType.Name;

        if (!string.IsNullOrWhiteSpace(options?.CallbackName))
        {
            headers[Headers.CallbackName] = options.CallbackName;
        }

        if (delayTime == null)
        {
            headers.Remove(Headers.DelayTime);
        }

        return headers;
    }

    private static string _ResolveCorrelationId(
        string? explicitCorrelationId,
        string? selectorCorrelationId,
        string? ambientCorrelationId,
        string messageId
    )
    {
        if (!string.IsNullOrWhiteSpace(explicitCorrelationId))
        {
            return explicitCorrelationId;
        }

        if (!string.IsNullOrWhiteSpace(selectorCorrelationId))
        {
            return selectorCorrelationId;
        }

        if (!string.IsNullOrWhiteSpace(ambientCorrelationId))
        {
            return ambientCorrelationId;
        }

        return messageId;
    }

    private static string? _ResolveCorrelationFromSelector<T>(
        MessageMetadata? metadata,
        T? contentObj,
        Type messageType
    )
    {
        if (metadata?.CorrelationSelector is null || contentObj is null)
        {
            return null;
        }

        try
        {
            var value = metadata.CorrelationSelector(contentObj);
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"CorrelationFrom selector failed for message type '{messageType.FullName ?? messageType.Name}'.",
                ex
            );
        }
    }

    private static void _ApplyProviderHeaderContributions<T>(
        Dictionary<string, string?> headers,
        MessageMetadata? metadata,
        T? contentObj,
        Type messageType
    )
    {
        if (metadata is null || contentObj is null || metadata.ProviderConfigs.Count == 0)
        {
            return;
        }

        foreach (var config in metadata.ProviderConfigs.Values)
        {
            if (config is not IProviderHeaderContributions providerContributions)
            {
                continue;
            }

            foreach (var contribution in providerContributions.HeaderContributions)
            {
                _ValidateProviderHeaderName(contribution.HeaderName, config);

                string? value;
                try
                {
                    value = contribution.Selector(contentObj);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(
                        $"Provider header contribution '{contribution.HeaderName}' failed for message type "
                            + $"'{messageType.FullName ?? messageType.Name}' from provider config "
                            + $"'{config.GetType().FullName ?? config.GetType().Name}'.",
                        ex
                    );
                }

                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                _ValidateProviderHeaderValue(contribution.HeaderName, value, config);
                headers[contribution.HeaderName] = value;
            }
        }
    }

    private static void _ValidateProviderHeaderName(string headerName, object providerConfig)
    {
        _ValidateHeaderName(headerName);

        if (_ProviderReservedHeaders.Contains(headerName))
        {
            throw new InvalidOperationException(
                $"Provider config '{providerConfig.GetType().FullName ?? providerConfig.GetType().Name}' tried to write "
                    + $"reserved header '{headerName}'."
            );
        }
    }

    private static void _ValidateProviderHeaderValue(string headerName, string value, object providerConfig)
    {
        if (_ContainsControlCharacter(value))
        {
            throw new InvalidOperationException(
                $"Provider config '{providerConfig.GetType().FullName ?? providerConfig.GetType().Name}' returned an invalid "
                    + $"value for header '{headerName}'. Header values cannot contain control characters."
            );
        }
    }

    private static void _ValidateHeaderName(string headerName)
    {
        Argument.IsNotNullOrWhiteSpace(headerName);

        if (_ContainsControlCharacter(headerName))
        {
            var safeHeaderName = LogSanitizer.Sanitize(headerName, 200);
            throw new InvalidOperationException(
                $"Header '{safeHeaderName}' is invalid. Header names cannot contain control characters."
            );
        }
    }

    private static void _ValidateHeaderValue(string headerName, string value)
    {
        if (_ContainsControlCharacter(value))
        {
            throw new InvalidOperationException(
                $"Header '{headerName}' contains an invalid value. Header values cannot contain control characters."
            );
        }
    }

    private static void _ValidateHeaderValues(Dictionary<string, string?> headers)
    {
        foreach (var (headerName, value) in headers)
        {
            if (value is null)
            {
                continue;
            }

            _ValidateHeaderValue(headerName, value);
        }
    }

    private static bool _ContainsControlCharacter(string value)
    {
        foreach (var character in value.AsSpan())
        {
            if (char.IsControl(character))
            {
                return true;
            }
        }

        return false;
    }

    private static string _ValidateMessageId(string messageId)
    {
        Argument.IsLessThanOrEqualTo(
            messageId.Length,
            MessageOptions.MessageIdMaxLength,
            $"Options.MessageId must be {MessageOptions.MessageIdMaxLength} characters or fewer before durable storage.",
            paramName: nameof(messageId)
        );

        _ValidateHeaderValue(Headers.MessageId, messageId);

        return messageId;
    }

    // Strict publish-time tenant integrity policy.
    //
    // U2 4-case header check (shipped in #228): MessageOptions.TenantId is the source of truth;
    // writing the wire header directly is reserved for transport-internal use. Whitespace raw
    // headers are treated as unset to mirror the lenient consume-side mapping in
    // TenantContextScope.ResolveTenantId.
    //
    // U10 ambient fallback (#238): when MessagingOptions.TenantContextRequired = true and the
    // typed property is unset, resolve from the ambient ICurrentTenant. If both are null, throw
    // MissingTenantContextException. Sibling of the EF (#234) and Mediator (#236) tenancy guards.
    //
    private void _ApplyTenantId(Dictionary<string, string?> headers, MessageOptions? options)
    {
        var typed = options?.TenantId;
        var rawPresent = headers.TryGetValue(Headers.TenantId, out var raw);
        var rawSet = rawPresent && !string.IsNullOrWhiteSpace(raw);

        // U2: typed unset, raw set → reject regardless of TenantContextRequired so injection
        // attempts cannot bypass by enabling strict tenancy.
        if (typed is null && rawSet)
        {
            var safeRawForReservedMessage = LogSanitizer.Sanitize(raw, MessageOptions.TenantIdMaxLength);

            var ex = new InvalidOperationException(
                $"Header '{Headers.TenantId}' is reserved. "
                    + "Use the typed TenantId property on your publish options to set the tenant identifier."
            )
            {
                Data = { ["Headers.TenantId.Raw"] = safeRawForReservedMessage },
            };

            throw ex;
        }

        // U10: typed unset (and raw unset since the path above rejected) — fall back to ambient
        // tenant when strict tenancy is required.
        if (typed is null && _options.TenantContextRequired)
        {
            typed = currentTenant.Id;
            if (string.IsNullOrWhiteSpace(typed))
            {
                var ex = new MissingTenantContextException(
                    "Publish requires an ambient tenant context but none was set. "
                        + "Set TenantId on your publish options explicitly, or wrap the publish in "
                        + "ICurrentTenant.Change(tenantId) to scope the AsyncLocal accessor "
                        + "(common pattern for background workers and IHostedService callers)."
                );
                throw ex;
            }
        }

        if (typed is null)
        {
            // Strip any whitespace-only key so transports do not see it.
            if (rawPresent)
            {
                headers.Remove(Headers.TenantId);
            }

            return;
        }

        _ValidateTenantId(typed);

        if (rawSet && !string.Equals(raw, typed, StringComparison.Ordinal))
        {
            // Sanitize wire-side raw value before interpolating into the exception message.
            // R4 delegates charset validation to consumers, so a malicious caller could otherwise
            // smuggle CR/LF/control chars into Exception.Message and downstream log sinks.
            var safeRaw = LogSanitizer.Sanitize(raw, MessageOptions.TenantIdMaxLength);

            var ex = new InvalidOperationException(
                $"Options.TenantId='{typed}' disagrees with header '{Headers.TenantId}'='{safeRaw}'. "
                    + "Set the typed property only."
            )
            {
                Data =
                {
                    [$"{nameof(PublishOptions)}.{nameof(PublishOptions.TenantId)}"] = typed,
                    ["Headers.TenantId.Raw"] = safeRaw,
                },
            };

            throw ex;
        }

        headers[Headers.TenantId] = typed;
    }

    private static void _ValidateTenantId(string tenantId)
    {
        Argument.IsNotNullOrWhiteSpace(tenantId);

        Argument.IsLessThanOrEqualTo(
            tenantId.Length,
            MessageOptions.TenantIdMaxLength,
            $"Options.TenantId must be {MessageOptions.TenantIdMaxLength} characters or fewer before durable storage.",
            paramName: nameof(tenantId)
        );

        _ValidateHeaderValue(Headers.TenantId, tenantId);
    }

    private static void _ValidateCustomHeaderNames(Dictionary<string, string?> headers)
    {
        foreach (var headerName in headers.Keys)
        {
            _ValidateHeaderName(headerName);

            if (_ReservedHeaders.Contains(headerName))
            {
                throw new InvalidOperationException(
                    $"Header '{headerName}' is reserved. Use the typed publish options properties for messaging metadata overrides."
                );
            }
        }
    }

    private DateTimeOffset _ResolvePublishAt(TimeSpan? delayTime)
    {
        var publishAt = timeProvider.GetUtcNow();
        if (delayTime is { } delay)
        {
            publishAt += delay;
        }

        return publishAt;
    }

    private string _ResolveMessageName(Type messageType, string? explicitMessageName)
    {
        if (!string.IsNullOrWhiteSpace(explicitMessageName))
        {
            MessagingOptions.ValidateMessageName(explicitMessageName);
            return _options.ApplyMessageNamePrefix(explicitMessageName);
        }

        if (_messageNameCache.TryGetValue(messageType, out var cachedName))
        {
            return cachedName;
        }

        if (consumerRegistry.TryGetRawMessageName(messageType, out var messageName))
        {
            messageName = _options.ApplyMessageNamePrefix(messageName);
            _messageNameCache.AddOrUpdate(messageType, messageName);
            return messageName;
        }

        if (_options.Conventions?.GetMessageName(messageType) is { } conventionMessageName)
        {
            conventionMessageName = _options.ApplyMessageNamePrefix(conventionMessageName);
            _messageNameCache.AddOrUpdate(messageType, conventionMessageName);
            return conventionMessageName;
        }

        throw new InvalidOperationException(
            $"No message name mapping found for message type '{messageType.Name}'. "
                + $"Register a message name mapping using WithMessageNameMapping<{messageType.Name}>(\"message-name\") "
                + "or set the MessageName property on your publish options explicitly."
        );
    }
}

internal sealed class PreparedPublishMessage
{
    public required string MessageName { get; init; }

    public required DateTime PublishAt { get; init; }

    public required Message Message { get; init; }

    public required IntentType IntentType { get; init; }
}
