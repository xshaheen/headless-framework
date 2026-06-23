// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Testing.Testcontainers;

/// <summary>
/// Scopes Testcontainers reuse to a single test project so each project reuses its OWN warm container
/// instead of sharing one.
/// </summary>
/// <remarks>
/// Testcontainers computes the reuse hash as a SHA over the whole container configuration — labels included.
/// Two projects that build an identically-configured container (for example several Redis fixtures with no
/// overrides, or several PostgreSQL fixtures that all pick the same database name) therefore collapse onto a
/// single shared reused container. That is fine when integration modules run one at a time, but collides once
/// modules run in parallel. Tagging each project's container with its (unique) test assembly name keeps every
/// project on its own reused container. Fixtures are always subclassed per project, so the runtime type's
/// assembly is the consuming project — never this shared package.
/// </remarks>
internal static class ReuseLabel
{
    /// <summary>Docker label key carrying the per-project reuse discriminator.</summary>
    public const string Key = "headless.fixture";

    /// <summary>Per-project reuse discriminator for <paramref name="fixture"/> (its test assembly name).</summary>
    public static string For(object fixture) => fixture.GetType().Assembly.GetName().Name ?? fixture.GetType().Name;
}
