// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.MultiTenancy;

/// <summary>
/// Where one tenant's data physically lives: its own database schema, its own database (a connection string), or
/// both. Resolved by <see cref="ITenantDataPlacementResolver"/> for a canonical tenant id.
/// </summary>
/// <remarks>
/// A sealed class rather than a record on purpose: a record's generated <see cref="object.ToString"/> would print
/// <see cref="ConnectionString"/>, and connection strings usually carry credentials. <see cref="ToString"/> here
/// reports only whether a connection string is set.
/// </remarks>
[PublicAPI]
public sealed class TenantDataPlacement
{
    /// <summary>Initializes a new <see cref="TenantDataPlacement"/>.</summary>
    /// <param name="schema">The tenant's database schema, or <see langword="null"/> to keep the context's own schema.</param>
    /// <param name="connectionString">
    /// The tenant's connection string, or <see langword="null"/> to keep the context's own database.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Both values are <see langword="null"/>, or a supplied value is empty or white space.
    /// </exception>
    public TenantDataPlacement(string? schema, string? connectionString)
    {
        if (schema is null && connectionString is null)
        {
            throw new ArgumentException(
                "A tenant data placement needs a schema, a connection string, or both.",
                nameof(schema)
            );
        }

        Schema = schema is null ? null : Argument.IsNotNullOrWhiteSpace(schema);
        ConnectionString = connectionString is null ? null : Argument.IsNotNullOrWhiteSpace(connectionString);
    }

    /// <summary>The tenant's database schema, or <see langword="null"/> when the tenant keeps the context's schema.</summary>
    public string? Schema { get; }

    /// <summary>
    /// The tenant's connection string, or <see langword="null"/> when the tenant keeps the context's database.
    /// Treat it as a secret: never log it.
    /// </summary>
    public string? ConnectionString { get; }

    /// <summary>Describes the placement without exposing <see cref="ConnectionString"/>.</summary>
    /// <returns>A description naming the schema and whether a connection string is set.</returns>
    public override string ToString()
    {
        return $"TenantDataPlacement {{ Schema = {Schema ?? "<context default>"}, ConnectionString = {(ConnectionString is null ? "<context default>" : "<redacted>")} }}";
    }
}
