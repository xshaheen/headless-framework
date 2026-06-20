// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Hosting.Seeders;

[PublicAPI]
public interface ISeeder
{
    ValueTask SeedAsync(CancellationToken cancellationToken = default);
}
