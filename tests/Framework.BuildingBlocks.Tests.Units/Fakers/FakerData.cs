// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests.Fakers;

public static class FakerData
{
    public static string GenerateName()
    {
        var faker = new Faker();
        return faker.Name.FullName();
    }
    public static string GenerateEmail()
    {
        var faker = new Faker();
        return faker.Internet.Email();
    }
}
