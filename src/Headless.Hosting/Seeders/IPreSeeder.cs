// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Hosting.Seeders;

public interface IPreSeeder
{
    ValueTask SeedAsync(CancellationToken cancellationToken = default);
}
