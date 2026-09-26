// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Idempotency;

/// <summary>
/// Storage-layer naming shared by every idempotency provider. Idempotency owns this setting so the record table lands
/// in the same schema whichever database backs it; a provider package contributes only the dialect rules used to
/// validate it.
/// </summary>
[PublicAPI]
public sealed class IdempotencyStorageOptions
{
    /// <summary>The schema the record table is created in when none is configured.</summary>
    public const string DefaultSchema = "idempotency";

    /// <summary>
    /// Gets or sets the database schema that holds the record table. Must be a valid identifier for the selected
    /// provider; validated on startup. Default: <see cref="DefaultSchema" /> (<c>"idempotency"</c>).
    /// </summary>
    public string Schema { get; set; } = DefaultSchema;
}
