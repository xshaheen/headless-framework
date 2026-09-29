// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting.Initialization;

namespace Headless.Fencing;

/// <summary>
/// Storage-layer naming shared by every fencing provider. Fencing owns this setting so the lease table lands in the
/// same schema whichever database backs it; a provider package contributes only the dialect rules used to validate
/// it.
/// </summary>
[PublicAPI]
public sealed class FencingStorageOptions
{
    /// <summary>
    /// Gets or sets the database schema that holds the lease table and its generation sequence. Must be a valid
    /// identifier for the selected provider; validated on startup. Default:
    /// <see cref="HeadlessStorageDefaults.Schema" /> (<c>"headless"</c>), the schema every Headless feature shares.
    /// </summary>
    public string Schema { get; set; } = HeadlessStorageDefaults.Schema;
}
