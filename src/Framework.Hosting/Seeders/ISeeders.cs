// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Hosting.Seeders;

public interface ISeeder
{
    ValueTask SeedAsync(CancellationToken cancellationToken = default);
}
