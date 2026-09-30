// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.MultiTenancy;

/// <summary>
/// Where one tenant's data physically lives: its own database schema, its own database (a connection string), both,
/// or the shared placement (<see cref="Shared"/>). Resolved by <see cref="ITenantDataPlacementResolver"/> for a
/// <see cref="TenantDataPlacementRequest"/>.
/// </summary>
/// <remarks>
/// A sealed class rather than a record on purpose: a record's generated <see cref="object.ToString"/> would print
/// <see cref="ConnectionString"/>, and connection strings usually carry credentials. <see cref="ToString"/> here
/// reports only whether a connection string is set.
/// </remarks>
[PublicAPI]
public sealed class TenantDataPlacement
{
    /// <summary>
    /// The shared placement: the tenant keeps the routed context's own registration, its <c>DefaultSchema</c> and
    /// its connection string, and the query filter separates it from the other shared tenants. Distinct from
    /// <see langword="null"/>, which means the tenant has no placement and is refused: a resolver returns
    /// <see cref="Shared"/> for a tenant it deliberately keeps in the shared database, so a hybrid fleet never
    /// repeats the shared connection string per tenant, and the answer is cached like any other placement.
    /// </summary>
    public static TenantDataPlacement Shared { get; } = new();

    private TenantDataPlacement()
    {
        IsShared = true;
    }

    /// <summary>Initializes a placement in the tenant's own schema, database, or both.</summary>
    /// <param name="schema">The tenant's database schema, or <see langword="null"/> to keep the context's own schema.</param>
    /// <param name="connectionString">
    /// The tenant's connection string, or <see langword="null"/> to keep the context's own database.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Both values are <see langword="null"/> (use <see cref="Shared"/> instead), or a supplied value is empty or white
    /// space.
    /// </exception>
    public TenantDataPlacement(string? schema, string? connectionString)
    {
        if (schema is null && connectionString is null)
        {
            throw new ArgumentException(
                "A tenant data placement needs a schema, a connection string, or both. Use TenantDataPlacement.Shared "
                    + "for a tenant that keeps the shared database and schema.",
                nameof(schema)
            );
        }

        Schema = schema is null ? null : Argument.IsNotNullOrWhiteSpace(schema);
        ConnectionString = connectionString is null ? null : Argument.IsNotNullOrWhiteSpace(connectionString);
    }

    /// <summary>Whether this is <see cref="Shared"/>: the tenant keeps the context's own schema and database.</summary>
    public bool IsShared { get; }

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
        if (IsShared)
        {
            return "TenantDataPlacement { Shared }";
        }

        return $"TenantDataPlacement {{ Schema = {Schema ?? "<context default>"}, ConnectionString = {(ConnectionString is null ? "<context default>" : "<redacted>")} }}";
    }
}
