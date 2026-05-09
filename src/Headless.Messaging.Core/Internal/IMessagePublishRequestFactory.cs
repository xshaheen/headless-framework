// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.Abstractions;
using Headless.Checks;
using Headless.Messaging.Configuration;
using Headless.Messaging.Messages;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.Internal;

internal interface IMessagePublishRequestFactory
{
    PreparedPublishMessage Create<T>(T? contentObj, PublishOptions? options = null, TimeSpan? delayTime = null);
}

internal sealed class MessagePublishRequestFactory(
    ILongIdGenerator idGenerator,
    TimeProvider timeProvider,
    IOptions<MessagingOptions> optionsAccessor,
    ICurrentTenant currentTenant
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
    };

    private readonly ConcurrentDictionary<Type, string> _topicNameCache = new();
    private readonly MessagingOptions _options = optionsAccessor.Value;
    private readonly ILongIdGenerator _idGenerator = idGenerator;
    private readonly TimeProvider _timeProvider = timeProvider;
    private readonly ICurrentTenant _currentTenant = currentTenant;

    public PreparedPublishMessage Create<T>(T? contentObj, PublishOptions? options = null, TimeSpan? delayTime = null)
    {
        if (delayTime is { } requestedDelay)
        {
            Argument.IsPositive(requestedDelay);
        }

        var topicName = _ResolveTopicName(typeof(T), options?.Topic);
        var headers = _CreateHeaders(typeof(T), topicName, options, delayTime);
        var publishAt = _ResolvePublishAt(delayTime);

        headers[Headers.SentTime] = publishAt.UtcDateTime.ToString(CultureInfo.InvariantCulture);

        if (delayTime.HasValue)
        {
            headers[Headers.DelayTime] = delayTime.Value.ToString();
        }

        return new PreparedPublishMessage
        {
            Topic = topicName,
            PublishAt = publishAt.UtcDateTime,
            Message = new Message(headers, contentObj),
        };
    }

    private Dictionary<string, string?> _CreateHeaders(
        Type messageType,
        string topicName,
        PublishOptions? options,
        TimeSpan? delayTime
    )
    {
        var headers =
            options?.Headers != null
                ? new Dictionary<string, string?>(options.Headers, StringComparer.Ordinal)
                : new Dictionary<string, string?>(StringComparer.Ordinal);

        _ValidateCustomHeaders(headers);
        _ApplyTenantId(headers, options);

        var messageId = string.IsNullOrWhiteSpace(options?.MessageId)
            ? _idGenerator.Create().ToString(CultureInfo.InvariantCulture)
            : _ValidateMessageId(options!.MessageId);

        headers[Headers.MessageId] = messageId;
        headers[Headers.CorrelationId] = string.IsNullOrWhiteSpace(options?.CorrelationId)
            ? messageId
            : options!.CorrelationId;
        headers[Headers.CorrelationSequence] = (options?.CorrelationSequence ?? 0).ToString(
            CultureInfo.InvariantCulture
        );
        headers[Headers.MessageName] = topicName;
        headers[Headers.Type] = messageType.Name;

        if (!string.IsNullOrWhiteSpace(options?.CallbackName))
        {
            headers[Headers.CallbackName] = options!.CallbackName;
        }

        if (delayTime == null)
        {
            headers.Remove(Headers.DelayTime);
        }

        return headers;
    }

    private static string _ValidateMessageId(string messageId)
    {
        Argument.IsLessThanOrEqualTo(
            messageId.Length,
            PublishOptions.MessageIdMaxLength,
            $"PublishOptions.MessageId must be {PublishOptions.MessageIdMaxLength} characters or fewer before durable storage.",
            paramName: nameof(messageId)
        );

        return messageId;
    }

    // Strict publish-time tenant integrity policy.
    //
    // U2 4-case header check (shipped in #228): PublishOptions.TenantId is the source of truth;
    // writing the wire header directly is reserved for transport-internal use. Whitespace raw
    // headers are treated as unset to mirror the lenient consume-side mapping in
    // ConsumeExecutionPipeline._ResolveTenantId.
    //
    // U10 ambient fallback (#238): when MessagingOptions.TenantContextRequired = true and the
    // typed property is unset, resolve from the ambient ICurrentTenant. If both are null, throw
    // MissingTenantContextException. Sibling of the EF (#234) and Mediator (#236) tenancy guards.
    //
    private void _ApplyTenantId(Dictionary<string, string?> headers, PublishOptions? options)
    {
        var typed = options?.TenantId;
        var rawPresent = headers.TryGetValue(Headers.TenantId, out var raw);
        var rawSet = rawPresent && !string.IsNullOrWhiteSpace(raw);

        // U2: typed unset, raw set → reject regardless of TenantContextRequired so injection
        // attempts cannot bypass by enabling strict tenancy.
        if (typed is null && rawSet)
        {
            var safeRawForReservedMessage = LogSanitizer.Sanitize(raw, PublishOptions.TenantIdMaxLength);
            var ex = new InvalidOperationException(
                $"Header '{Headers.TenantId}' is reserved. "
                    + $"Use {nameof(PublishOptions)}.{nameof(PublishOptions.TenantId)} to set the tenant identifier."
            );
            ex.Data["Headers.TenantId.Raw"] = safeRawForReservedMessage;
            throw ex;
        }

        // U10: typed unset (and raw unset since the path above rejected) — fall back to ambient
        // tenant when strict tenancy is required.
        if (typed is null && _options.TenantContextRequired)
        {
            typed = _currentTenant.Id;
            if (string.IsNullOrWhiteSpace(typed))
            {
                var ex = new MissingTenantContextException(
                    "Publish requires an ambient tenant context but none was set. "
                        + "Set PublishOptions.TenantId explicitly, or wrap the publish in "
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
            var safeRaw = LogSanitizer.Sanitize(raw, PublishOptions.TenantIdMaxLength);
            var ex = new InvalidOperationException(
                $"PublishOptions.TenantId='{typed}' disagrees with header '{Headers.TenantId}'='{safeRaw}'. "
                    + "Set the typed property only."
            );
            ex.Data[$"{nameof(PublishOptions)}.{nameof(PublishOptions.TenantId)}"] = typed;
            ex.Data["Headers.TenantId.Raw"] = safeRaw;
            throw ex;
        }

        headers[Headers.TenantId] = typed;
    }

    private static void _ValidateTenantId(string tenantId)
    {
        Argument.IsNotNullOrWhiteSpace(tenantId, paramName: nameof(tenantId));
        Argument.IsLessThanOrEqualTo(
            tenantId.Length,
            PublishOptions.TenantIdMaxLength,
            $"PublishOptions.TenantId must be {PublishOptions.TenantIdMaxLength} characters or fewer before durable storage.",
            paramName: nameof(tenantId)
        );
    }

    private void _ValidateCustomHeaders(IReadOnlyDictionary<string, string?> headers)
    {
        var invalidHeader = headers.Keys.FirstOrDefault(_ReservedHeaders.Contains);
        if (invalidHeader != null)
        {
            throw new InvalidOperationException(
                $"Header '{invalidHeader}' is reserved. Use {nameof(PublishOptions)} for messaging metadata overrides."
            );
        }
    }

    private DateTimeOffset _ResolvePublishAt(TimeSpan? delayTime)
    {
        var publishAt = _timeProvider.GetUtcNow();
        if (delayTime is { } delay)
        {
            publishAt += delay;
        }

        return publishAt;
    }

    private string _ResolveTopicName(Type messageType, string? explicitTopic)
    {
        if (!string.IsNullOrWhiteSpace(explicitTopic))
        {
            return _options.ApplyTopicNamePrefix(explicitTopic!);
        }

        if (_topicNameCache.TryGetValue(messageType, out var cachedName))
        {
            return cachedName;
        }

        if (_options.TopicMappings.TryGetValue(messageType, out var topicName))
        {
            topicName = _options.ApplyTopicNamePrefix(topicName);
            _topicNameCache.TryAdd(messageType, topicName);
            return topicName;
        }

        if (_options.Conventions?.GetTopicName(messageType) is { } conventionTopic)
        {
            conventionTopic = _options.ApplyTopicNamePrefix(conventionTopic);
            _topicNameCache.TryAdd(messageType, conventionTopic);
            return conventionTopic;
        }

        throw new InvalidOperationException(
            $"No topic mapping found for message type '{messageType.Name}'. "
                + $"Register a topic mapping using WithTopicMapping<{messageType.Name}>(\"topic-name\") "
                + $"or set {nameof(PublishOptions)}.{nameof(PublishOptions.Topic)} explicitly."
        );
    }
}

internal sealed class PreparedPublishMessage
{
    public required string Topic { get; init; }

    public required DateTime PublishAt { get; init; }

    public required Message Message { get; init; }
}
