// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Hosting;

/// <summary>
/// Writes data the application's code declares, such as definitions or scheduled jobs, into its stores. Unlike a
/// resource provisioner, a seeder writes rows, not structure, and must run again whenever the declarations change.
/// </summary>
/// <remarks>
/// <para>
/// The host start never seeds by default: the deployment runs the <c>seed</c> (or <c>deploy</c>) verb of
/// <c>RunHeadlessAsync</c>, and a development or test host that should seed as it starts calls
/// <c>AddDeployStepsOnStartup</c>.
/// </para>
/// <para>
/// Seeders run one at a time, each after the seeders it names in <see cref="DependsOn" />, and otherwise in
/// registration order. Make <see cref="SeedAsync" /> idempotent: it runs on every deployment.
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

    /// <summary>Writes the code-declared data into the store.</summary>
    /// <param name="cancellationToken">Cancels the seeding.</param>
    /// <returns>A task that completes when the data is written.</returns>
    Task SeedAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Reports, without writing, whether the store already holds what <see cref="SeedAsync" /> would write, so
    /// <c>seed verify</c> can catch a deployment that skipped the <c>seed</c> step. Default:
    /// <see cref="SeedState.Unknown" />, for a seeder that cannot tell.
    /// </summary>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <returns>Whether the store is current.</returns>
    Task<SeedState> VerifyAsync(CancellationToken cancellationToken) => Task.FromResult(SeedState.Unknown);

    /// <summary>
    /// Selects, from every registered seeder, the ones that must run before this one, for example the seeder that writes
    /// the rows this one references. Default: none.
    /// </summary>
    /// <param name="seeders">Every registered seeder, this one included.</param>
    /// <returns>The seeders to run first.</returns>
    IEnumerable<ISeeder> DependsOn(IReadOnlyList<ISeeder> seeders) => [];
}

/// <summary>What <see cref="ISeeder.VerifyAsync" /> found.</summary>
[PublicAPI]
public enum SeedState
{
    /// <summary>The seeder cannot tell whether its store is current.</summary>
    Unknown = 0,

    /// <summary>The store holds everything the code declares.</summary>
    Current = 1,

    /// <summary>The store lacks or disagrees with what the code declares: run <c>seed</c>.</summary>
    Stale = 2,
}
