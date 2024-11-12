// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Bogus;

public static class FakerExtensions
{
    private static readonly bool?[] _Values = [true, false, null];

    public static bool? NullableBool(this Faker faker)
    {
        return faker.PickRandom(_Values);
    }
}
