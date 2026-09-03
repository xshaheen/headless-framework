// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Api.Concurrency;

/// <summary>Provides the decoded strong entity tag supplied by the current request.</summary>
[PublicAPI]
public sealed class IfMatchContext
{
    /// <summary>Gets the decoded entity tag, or an empty value when no precondition was required.</summary>
    public ReadOnlyMemory<byte> ETag { get; internal set; }

    /// <summary>Gets whether the request supplied a valid entity tag.</summary>
    public bool HasValue => !ETag.IsEmpty;
}
