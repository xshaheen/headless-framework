// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Hosting;

/// <summary>
/// Writes data the application's code declares, such as definitions or scheduled jobs, into its stores. Unlike a
/// stateful resource, a seeder writes rows, not structure, and must run again whenever the declarations change.
/// </summary>
/// <remarks>
/// <para>
/// The host start never seeds by default: the deployment runs the <c>seed</c> verb of <c>RunHeadlessAsync</c>, and a
/// development or test host that should seed as it starts calls <c>AddHeadlessSeedOnStartup</c>.
/// </para>
/// <para>
/// Seeders run one at a time, ordered by <see cref="SeederPriorityAttribute"/> (lower first), each resolved from one
/// DI scope. Make <see cref="SeedAsync"/> idempotent: it runs on every deployment.
/// </para>
/// </remarks>
[PublicAPI]
public interface ISeeder
{
    /// <summary>
    /// Gets the seeder's name, printed by the <c>seed</c> verb and matched by its <c>--name</c> filter. Defaults to the
    /// type name.
    /// </summary>
    string Name => GetType().Name;

    /// <summary>Performs the seeding operation.</summary>
    /// <param name="cancellationToken">A token to cancel seeding.</param>
    ValueTask SeedAsync(CancellationToken cancellationToken = default);
}
