// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.DistributedLocks;

/// <summary>
/// Storage-layer naming shared by every relational distributed-lock provider. The feature owns this setting so the
/// fencing sequence lands in the same schema whichever database backs it; a provider package contributes only the
/// dialect rules used to validate it. Providers that create no schema-bound object (Redis, in-memory) ignore it.
/// </summary>
[PublicAPI]
public sealed class DistributedLocksStorageOptions
{
    /// <summary>The schema the fencing sequence is created in when none is configured.</summary>
    public const string DefaultSchema = "locks";

    /// <summary>
    /// Gets or sets the database schema that holds the fencing sequence the relational providers use to stamp
    /// each exclusive acquisition with a strictly-increasing token. Must be a valid identifier for the selected
    /// provider; validated on startup. Default: <see cref="DefaultSchema"/> (<c>"locks"</c>).
    /// </summary>
    public string Schema { get; set; } = DefaultSchema;
}
