// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Fencing;

/// <summary>
/// Storage-layer naming shared by every fencing provider. Fencing owns this setting so the lease table lands in the
/// same schema whichever database backs it; a provider package contributes only the dialect rules used to validate
/// it.
/// </summary>
[PublicAPI]
public sealed class FencingStorageOptions
{
    /// <summary>The schema the lease table and generation sequence are created in when none is configured.</summary>
    public const string DefaultSchema = "fencing";

    /// <summary>
    /// Gets or sets the database schema that holds the lease table and its generation sequence. Must be a valid
    /// identifier for the selected provider; validated on startup. Default: <see cref="DefaultSchema" />
    /// (<c>"fencing"</c>).
    /// </summary>
    public string Schema { get; set; } = DefaultSchema;
}
