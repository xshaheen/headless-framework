// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Bogus;

namespace Tests;

/// <summary>
/// Provider-neutral seed input for <see cref="ITenantCatalogStoreFixture.SeedAsync"/>. <see cref="Identifier"/>
/// is always already-normalized (trimmed, lowercase) — the conformance suite proves stores compare it
/// ordinally and never re-normalize (R7); normalization-equivalence itself is a catalog-service concern
/// tested in <c>Headless.MultiTenancy.Tests.Unit</c>. <see cref="ExtraProperties"/> values are plain
/// strings only: that is the lowest common denominator every v1 store can seed natively (the
/// configuration store's <c>ConfigurationTenantSeed.ExtraProperties</c> is <c>IDictionary&lt;string,string&gt;</c>),
/// so the conformance suite never trips a provider-specific value round-trip mismatch (e.g. JSON number
/// widening).
/// </summary>
public sealed record TenantSeed(
    string Id,
    string Identifier,
    string? Name = null,
    bool IsEnabled = true,
    IReadOnlyDictionary<string, string>? ExtraProperties = null
);

/// <summary>Builds valid, randomized <see cref="TenantSeed"/> instances for conformance tests.</summary>
public static class TenantSeedFaker
{
    /// <summary>
    /// Creates a <see cref="TenantSeed"/> with a random DNS-label-shaped identifier (lowercase letters,
    /// digits, and hyphens only) so it is accepted by every store, including the configuration store's
    /// default identifier-shape validation.
    /// </summary>
    /// <param name="faker">The <see cref="Faker"/> instance to draw random values from.</param>
    /// <param name="identifier">An explicit already-normalized identifier, or <see langword="null"/> to generate one.</param>
    /// <param name="id">An explicit canonical tenant id, or <see langword="null"/> to generate one.</param>
    /// <param name="isEnabled">Whether the seeded tenant is enabled.</param>
    public static TenantSeed Create(Faker faker, string? identifier = null, string? id = null, bool isEnabled = true)
    {
        var suffix = faker.Random.Hexadecimal(8, prefix: "").ToLowerInvariant();

        return new TenantSeed(
            Id: id ?? $"ten_{suffix}",
            Identifier: identifier ?? $"tenant-{suffix}",
            Name: faker.Company.CompanyName(),
            IsEnabled: isEnabled
        );
    }
}
