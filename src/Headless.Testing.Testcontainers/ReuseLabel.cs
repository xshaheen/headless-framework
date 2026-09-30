// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Testing.Testcontainers;

/// <summary>
/// Scopes Testcontainers reuse to one test project in one checkout, so each project reuses its OWN warm container
/// instead of sharing one with another project or another checkout of the repository.
/// </summary>
/// <remarks>
/// <para>
/// Testcontainers computes the reuse hash as a SHA over the whole container configuration — labels included.
/// Two projects that build an identically-configured container (for example several Redis fixtures with no
/// overrides, or several PostgreSQL fixtures that all pick the same database name) therefore collapse onto a
/// single shared reused container. That is fine when integration modules run one at a time, but collides once
/// modules run in parallel. Tagging each project's container with its (unique) test assembly name keeps every
/// project on its own reused container. Fixtures are always subclassed per project, so the runtime type's
/// assembly is the consuming project — never this shared package.
/// </para>
/// <para>
/// The assembly name is the same in every checkout, so two worktrees of the repository running the same
/// integration project would still share one container, and one worktree's schema, rows, and container restarts
/// would break the other's run. The checkout label adds the checkout's root directory to the hash, and doubles as
/// the way to find the containers a removed worktree left behind.
/// </para>
/// </remarks>
internal static class ReuseLabel
{
    /// <summary>Docker label key carrying the per-project reuse discriminator.</summary>
    public const string Key = "headless.fixture";

    /// <summary>Docker label key carrying the per-checkout reuse discriminator.</summary>
    public const string CheckoutKey = "headless.checkout";

    /// <summary>Gets the checkout these test binaries were built in: the root directory of the repository checkout.</summary>
    public static string Checkout { get; } = ForCheckout(AppContext.BaseDirectory);

    /// <summary>Per-project reuse discriminator for <paramref name="fixture"/> (its test assembly name).</summary>
    public static string For(object fixture)
    {
        return fixture.GetType().Assembly.GetName().Name ?? fixture.GetType().Name;
    }

    /// <summary>
    /// Returns the checkout that contains <paramref name="baseDirectory" />: the nearest ancestor holding a
    /// <c>.git</c> entry (a directory in the main checkout, a file in a worktree), or the directory itself outside a
    /// repository, so binaries built anywhere else still get a stable, distinct label.
    /// </summary>
    internal static string ForCheckout(string baseDirectory)
    {
        var start = Path.TrimEndingDirectorySeparator(Path.GetFullPath(baseDirectory));

        for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
        {
            var git = Path.Combine(directory.FullName, ".git");

            if (Directory.Exists(git) || File.Exists(git))
            {
                return Path.TrimEndingDirectorySeparator(directory.FullName);
            }
        }

        return start;
    }
}
