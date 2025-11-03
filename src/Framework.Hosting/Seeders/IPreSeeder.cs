// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Hosting.Seeders;

public interface IPreSeeder
{
    ValueTask SeedAsync(CancellationToken cancellationToken = default);
}
