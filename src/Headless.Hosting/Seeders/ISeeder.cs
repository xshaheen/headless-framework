// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Hosting.Seeders;

/// <summary>Defines a unit of work that seeds the data store during startup or deployment.</summary>
/// <remarks>
/// Seeders are discovered and executed by <c>SeedAsync</c> on the root <see cref="IServiceProvider"/>.
/// Use <see cref="SeederPriorityAttribute"/> to control execution order when multiple seeders are
/// registered. Each seeder runs in its own DI scope when executed in parallel mode.
/// </remarks>
[PublicAPI]
public interface ISeeder
{
    /// <summary>Performs the seeding operation.</summary>
    /// <param name="cancellationToken">A token to cancel seeding.</param>
    ValueTask SeedAsync(CancellationToken cancellationToken = default);
}
