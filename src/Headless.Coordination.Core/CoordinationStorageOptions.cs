// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Coordination;

/// <summary>
/// Storage-layer naming shared by every relational coordination provider. Coordination owns this setting so the
/// membership tables land in the same schema whichever database backs them; a provider package contributes only
/// the dialect rules used to validate it. Providers with no schema concept (Redis) ignore these options.
/// </summary>
[PublicAPI]
public sealed class CoordinationStorageOptions
{
    /// <summary>The schema the coordination membership tables are created in when none is configured.</summary>
    public const string DefaultSchema = "coordination";

    /// <summary>
    /// Gets or sets the database schema that holds the coordination membership tables
    /// (<c>coordination_node_generation</c>, <c>coordination_descriptor</c>, <c>coordination_liveness</c> and
    /// their SQL Server equivalents). Must be a valid identifier for the selected provider; validated on startup.
    /// Default: <see cref="DefaultSchema"/> (<c>"coordination"</c>).
    /// </summary>
    public string Schema { get; set; } = DefaultSchema;
}
