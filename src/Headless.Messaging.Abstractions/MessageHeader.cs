// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.ObjectModel;

namespace Headless.Messaging;

/// <summary>
/// A read-only view of the message headers.
/// </summary>
/// <remarks>
/// The dictionary is copied at construction time using ordinal key comparison, so mutation of the
/// original source dictionary after construction has no effect. Direct key mutation is unsupported.
/// Response and callback mutations are managed on <see cref="ConsumeContext"/>.
/// </remarks>
[PublicAPI]
public sealed class MessageHeader : ReadOnlyDictionary<string, string?>
{
    /// <summary>Initializes a new snapshot from the supplied header dictionary.</summary>
    /// <param name="dictionary">The source headers to copy.</param>
    public MessageHeader(IDictionary<string, string?> dictionary)
        : base(new Dictionary<string, string?>(dictionary, StringComparer.Ordinal)) { }

    internal MessageHeader()
        : base(new Dictionary<string, string?>(StringComparer.Ordinal)) { }
}
