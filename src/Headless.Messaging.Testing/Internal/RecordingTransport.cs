// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Reflection;
using Headless.Messaging.Messages;
using Headless.Messaging.Serialization;
using Headless.Messaging.Transport;

namespace Headless.Messaging.Testing.Internal;

internal sealed class RecordingTransport(ITransport inner, MessageObservationStore store, ISerializer serializer)
    : ITransport
{
    private static readonly ConcurrentDictionary<string, Type?> _TypeCache = new(StringComparer.Ordinal);
    public BrokerAddress BrokerAddress => inner.BrokerAddress;

    public async Task<OperateResult> SendAsync(TransportMessage message, CancellationToken cancellationToken = default)
    {
        var result = await inner.SendAsync(message, cancellationToken).ConfigureAwait(false);

        if (result.Succeeded)
        {
            var headers = message.Headers;
            var messageTypeName = headers.TryGetValue(Headers.Type, out var typeName) ? typeName : null;

            object messageObj = message;
            Type messageType = typeof(TransportMessage);

            if (message.Body.Length > 0 && messageTypeName != null)
            {
                var resolvedType = _ResolveType(messageTypeName);

                if (resolvedType != null)
                {
                    try
                    {
                        var deserialized = await serializer
                            .DeserializeAsync(message, resolvedType)
                            .ConfigureAwait(false);

                        if (deserialized.Value != null)
                        {
                            messageObj = deserialized.Value;
                            messageType = resolvedType;
                        }
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException)
                    {
                        // Surface deserialization failures immediately rather than causing
                        // cryptic WaitForPublished<T> timeouts when the type never matches.
                        throw new InvalidOperationException(
                            $"RecordingTransport: failed to deserialize published message of type '{messageTypeName}'. "
                                + "Verify serializer configuration matches the message contract.",
                            ex
                        );
                    }
                }
            }

            var recorded = RecordedMessage.FromHeaders(headers, messageObj, messageType);

            store.Record(recorded, MessageObservationType.Published);
        }

        return result;
    }

    public ValueTask DisposeAsync() => inner.DisposeAsync();

    /// <summary>
    /// Resolves a CLR type from the header value. The framework may write either
    /// an assembly-qualified name or a short type name; this method handles both.
    /// </summary>
    private static Type? _ResolveType(string typeName) =>
        _TypeCache.GetOrAdd(
            typeName,
            static name =>
            {
                // Try assembly-qualified name first (fast path)
                var type = Type.GetType(name);
                if (type != null)
                {
                    return type;
                }

                // Fall back to scanning loaded assemblies by full name or short name.
                // This covers the case where the framework writes messageType.Name.
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    // Skip dynamic assemblies — they cannot be scanned reliably
                    if (assembly.IsDynamic)
                    {
                        continue;
                    }

                    try
                    {
                        foreach (var candidate in assembly.GetExportedTypes())
                        {
                            if (candidate.FullName == name || candidate.Name == name)
                            {
                                return candidate;
                            }
                        }
                    }
                    catch (ReflectionTypeLoadException)
                    {
                        // Some assemblies may fail to load types — skip them
                    }
                }

                return null;
            }
        );
}
