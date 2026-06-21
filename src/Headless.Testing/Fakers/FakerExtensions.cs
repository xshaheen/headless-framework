// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Bogus;

/// <summary>Extensions on <see cref="Faker"/> for generating nullable and tri-state values.</summary>
[PublicAPI]
public static class FakerExtensions
{
    private static readonly bool?[] _Values = [true, false, null];

    /// <summary>
    /// Returns a randomly chosen value from <see langword="true"/>, <see langword="false"/>,
    /// and <see langword="null"/> with equal probability.
    /// </summary>
    /// <param name="faker">The Bogus faker instance.</param>
    /// <returns>A nullable boolean.</returns>
    public static bool? NullableBool(this Faker faker)
    {
        return faker.PickRandom(_Values);
    }
}
