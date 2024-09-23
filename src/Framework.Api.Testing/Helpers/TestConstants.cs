// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Bogus;

namespace Framework.Api.Testing.Helpers;

public static class TestConstants
{
    public static readonly Faker F = new();

    static TestConstants()
    {
        AutoBogus.AutoFaker.Configure(builder =>
        {
            builder.WithFakerHub(F).WithRecursiveDepth(3);
        });
    }
}
