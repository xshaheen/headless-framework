// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Sql;

[PublicAPI]
public interface IConnectionStringChecker
{
    Task<(bool Connected, bool DatabaseExists)> CheckAsync(string connectionString);
}
