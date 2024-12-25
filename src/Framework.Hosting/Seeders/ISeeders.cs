// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Hosting.Seeders;

public interface ISeeder
{
    public ValueTask SeedAsync();
}
