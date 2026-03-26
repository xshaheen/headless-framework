// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Headless.Messaging.Messages;
using Headless.Messaging.Serialization;
using Headless.Messaging.Transport;

namespace Headless.Messaging.Testing.Internal;

internal sealed class RecordingTransport(ITransport inner, MessageObservationStore store, ISerializer serializer)
    : ITransport
{
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
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex) when (ex is not OutOfMemoryException)
                    {
                        // Deserialization failure is non-fatal; keep raw TransportMessage as the recorded payload.
                        // Narrow catch ensures unexpected infrastructure bugs surface as diagnostics rather than
                        // cryptic timeouts in WaitForPublished<T>.
                        System.Diagnostics.Debug.WriteLine(
                            $"RecordingTransport: deserialization failed for type '{messageTypeName}': {ex.Message}"
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
    private static Type? _ResolveType(string typeName)
    {
        // Try assembly-qualified name first (fast path)
        var type = Type.GetType(typeName);
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
                    if (candidate.FullName == typeName || candidate.Name == typeName)
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
}
