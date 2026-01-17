// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.ObjectModel;

namespace Framework.Messages.Messages;

public sealed class MessageHeader(IDictionary<string, string?> dictionary)
    : ReadOnlyDictionary<string, string?>(dictionary)
{
    internal IDictionary<string, string?>? ResponseHeader { get; set; }

    /// <summary>
    /// When a callbackName is specified from publish message, use this method to add an additional header.
    /// </summary>
    /// <param name="key">The response header key.</param>
    /// <param name="value">The response header value.</param>
    public void AddResponseHeader(string key, string? value)
    {
        ResponseHeader ??= new Dictionary<string, string?>(StringComparer.Ordinal);
        ResponseHeader[key] = value;
    }

    /// <summary>
    /// When a callbackName is specified from publish message, use this method to abort the callback.
    /// </summary>
    public void RemoveCallback()
    {
        Dictionary.Remove(Headers.CallbackName);
    }

    /// <summary>
    /// When a callbackName is specified from Publish message, use this method to rewrite the callback name.
    /// </summary>
    /// <param name="callbackName">The new callback name.</param>
    public void RewriteCallback(string callbackName)
    {
        Dictionary[Headers.CallbackName] = callbackName;
    }
}
