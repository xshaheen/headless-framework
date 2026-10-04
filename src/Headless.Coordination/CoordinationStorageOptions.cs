// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;

namespace Headless.Coordination;

/// <summary>
/// Storage-layer naming shared by every relational coordination provider. Coordination owns this setting so the
/// membership tables land in the same schema whichever database backs them; a provider package contributes only
/// the dialect rules used to validate it. Providers with no schema concept (Redis) ignore these options.
/// </summary>
[PublicAPI]
public sealed class CoordinationStorageOptions
{
    /// <summary>
    /// Gets or sets the database schema that holds the coordination membership tables
    /// (<c>coordination_node_generation</c>, <c>coordination_descriptor</c>, <c>coordination_liveness</c> and
    /// their SQL Server equivalents). Must be a valid identifier for the selected provider; validated on startup.
    /// Default: <see cref="HeadlessStorageDefaults.Schema"/> (<c>"headless"</c>), the schema every Headless feature
    /// shares.
    /// </summary>
    public string Schema { get; set; } = HeadlessStorageDefaults.Schema;
}
