// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;

namespace Headless.Idempotency;

/// <summary>
/// Storage-layer naming shared by every idempotency provider. Idempotency owns this setting so the record table lands
/// in the same schema whichever database backs it; a provider package contributes only the dialect rules used to
/// validate it.
/// </summary>
[PublicAPI]
public sealed class IdempotencyStorageOptions
{
    /// <summary>
    /// Gets or sets the database schema that holds the record table. Must be a valid identifier for the selected
    /// provider; validated on startup. Default: <see cref="HeadlessStorageDefaults.Schema" /> (<c>"headless"</c>),
    /// the schema every Headless feature shares.
    /// </summary>
    public string Schema { get; set; } = HeadlessStorageDefaults.Schema;
}
