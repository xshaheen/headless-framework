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
    IOptions<MessagingOptions> optionsAccessor
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
        var headers = options?.Headers != null
            ? new Dictionary<string, string?>(options.Headers, StringComparer.Ordinal)
            : new Dictionary<string, string?>(StringComparer.Ordinal);

        _ValidateCustomHeaders(headers);

        var messageId = string.IsNullOrWhiteSpace(options?.MessageId)
            ? _idGenerator.Create().ToString(CultureInfo.InvariantCulture)
            : options!.MessageId;

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
