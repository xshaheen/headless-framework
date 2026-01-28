// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Hosting.Seeders;

public interface ISeeder
{
    ValueTask SeedAsync(CancellationToken cancellationToken = default);
}
