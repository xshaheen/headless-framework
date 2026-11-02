// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

/// <summary>Generates Paymob Cash Out test values from a per-test seeded <see cref="Faker"/>.</summary>
internal static class CashOutTestData
{
    /// <summary>
    /// Creates a <see cref="Faker"/> seeded from the running test's display name, so each test draws a
    /// stable sequence: a failure reproduces on re-run, and tests that share the class fixture's WireMock
    /// server do not generate the same request bodies.
    /// </summary>
    public static Faker CreateFaker()
    {
        var testName = TestContext.Current.Test?.TestDisplayName ?? nameof(CashOutTestData);

        return new Faker { Random = new Randomizer(localSeed: _StableHash(testName)) };
    }

    public static string Text(Faker f) => f.Random.AlphaNumeric(24);

    // FNV-1a: string.GetHashCode is randomized per process, so it cannot give a seed that holds across runs.
    private static int _StableHash(string value)
    {
        unchecked
        {
            var hash = (int)2166136261;

            foreach (var c in value)
            {
                hash = (hash ^ c) * 16777619;
            }

            return hash;
        }
    }
}
