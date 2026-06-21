// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.ObjectModel;

namespace Headless.Messaging;

/// <summary>
/// A read-only view of the current message headers that also exposes consumer-side mutation hooks
/// for callback and response-header operations.
/// </summary>
/// <remarks>
/// The dictionary is copied at construction time using ordinal key comparison, so mutation of the
/// original source dictionary after construction has no effect. Direct key mutation is intentionally
/// unsupported; use <see cref="AddResponseHeader"/>, <see cref="RemoveCallback"/>, and
/// <see cref="RewriteCallback"/> for the supported write paths inside a consumer handler.
/// </remarks>
[PublicAPI]
public sealed class MessageHeader(IDictionary<string, string?> dictionary)
    : ReadOnlyDictionary<string, string?>(new Dictionary<string, string?>(dictionary, StringComparer.Ordinal))
{
    internal IDictionary<string, string?>? ResponseHeader { get; set; }

    /// <summary>
    /// Adds or overwrites a response header that is forwarded to the callback subscriber.
    /// Call this inside a consumer handler when a callback name is present in the message headers
    /// and the consumer needs to pass extra data back to the original publisher.
    /// </summary>
    /// <param name="key">The response header key.</param>
    /// <param name="value">The response header value.</param>
    public void AddResponseHeader(string key, string? value)
    {
        ResponseHeader ??= new Dictionary<string, string?>(StringComparer.Ordinal);
        ResponseHeader[key] = value;
    }

    /// <summary>
    /// Removes the callback name from the message headers, preventing the framework from invoking
    /// any callback subscriber after the current handler completes.
    /// </summary>
    public void RemoveCallback()
    {
        Dictionary.Remove(Headers.CallbackName);
    }

    /// <summary>
    /// Replaces the callback name in the message headers, redirecting the post-consume callback
    /// invocation to a different subscriber than the one originally specified by the publisher.
    /// </summary>
    /// <param name="callbackName">The replacement callback subscriber name.</param>
    public void RewriteCallback(string callbackName)
    {
        Dictionary[Headers.CallbackName] = callbackName;
    }
}
